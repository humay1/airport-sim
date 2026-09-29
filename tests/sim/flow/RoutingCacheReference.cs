using System;
using System.Collections.Generic;
using System.Text.Json;
using AirportSim.Sim.Core;
using AirportSim.Sim.World;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>Test-authored queue profile values (04), never data/ balance values.</summary>
    internal readonly struct RefQueueProfile
    {
        public RefQueueProfile(Fx rate, int capacity, Fx threshold, Fx hysteresis)
        {
            Rate = rate;
            Capacity = capacity;
            Threshold = threshold;
            Hysteresis = hysteresis;
        }

        public Fx Rate { get; }

        public int Capacity { get; }

        public Fx Threshold { get; }

        public Fx Hysteresis { get; }
    }

    internal enum RefKind
    {
        Source,
        Corridor,
        Hall,
        Queue,
        Gate,
        Sink,
    }

    internal sealed class RefCohort
    {
        public ulong Id;
        public CohortKey Key;
        public uint Node;
        public int Count;
        public ulong Entered;
        public ulong Due;
        public bool Open;
        public uint Held;
        public uint By;
    }

    internal sealed class RefQueue
    {
        public int ServerCount;
        public int Open;
        public RefQueueProfile Profile;
        public Fx Credit = Fx.Zero;
        public bool Flag;
    }

    /// <summary>
    /// The lockstep, uncached reference model of 09 §9.6 "The reference" (Q-036):
    /// §9.12's whole Tick (snapshot, movement, merge, thresholds), §9.6's routing
    /// rule evaluated from scratch for every released cohort, §9.3's id rule and
    /// §9.7's Inject and Absorb. Its inputs are the flow-graph JSON the test
    /// writes, the test's own profile values and IWorldSystem's public queries.
    /// Its ids only order cohorts; they are never compared with the system's.
    /// </summary>
    internal sealed class FlowReference
    {
        public static readonly Fx Epsilon = Fx.FromRatio(1, 1000);

        private readonly IWorldSystem _world;
        private readonly SortedDictionary<uint, RefKind> _kind = new SortedDictionary<uint, RefKind>();
        private readonly SortedDictionary<uint, RefQueue> _queues = new SortedDictionary<uint, RefQueue>();
        private readonly Dictionary<string, Fx> _speed;
        private readonly List<uint> _gates = new List<uint>();

        // Always ascending by Id: new ids are larger than every live one, and
        // removal and merge keep the order.
        private readonly List<RefCohort> _cohorts = new List<RefCohort>();
        private ulong _nextId = 1;
        private ulong _ticksDone;
        private bool _commandThisTick;
        private Dictionary<(uint, long), uint> _routedLastTick = new Dictionary<(uint, long), uint>();
        private ulong _routedLastTickAt = ulong.MaxValue;

        /// <summary>The current tick's observable events, as canonical lines.</summary>
        public readonly List<string> Events = new List<string>();

        // Coverage of §9.6's required script cases, counted by the reference.
        public int WalkSpeedSplits;
        public int GateTieBreaks;
        public int EdgeTieBreaks;
        public int LaneChangeFlips;
        public int BlockedRerouteMoved;
        public int BlockedRerouteReblocked;
        public int ShowUpSpikes;
        public int MissedWhileBlocked;
        public int Refusals;

        public FlowReference(IWorldSystem world, byte[] flowJson, IReadOnlyDictionary<string, RefQueueProfile> queueProfiles, IReadOnlyDictionary<string, Fx> walkSpeeds)
        {
            _world = world;
            _speed = new Dictionary<string, Fx>();
            foreach (KeyValuePair<string, Fx> kv in walkSpeeds)
            {
                _speed[kv.Key] = kv.Value;
            }

            using (JsonDocument doc = JsonDocument.Parse(flowJson))
            {
                foreach (JsonElement n in doc.RootElement.GetProperty("nodes").EnumerateArray())
                {
                    uint id = n.GetProperty("id").GetUInt32();
                    string kind = n.GetProperty("kind").GetString()!;
                    RefKind k = kind switch
                    {
                        "source" => RefKind.Source,
                        "corridor" => RefKind.Corridor,
                        "hall" => RefKind.Hall,
                        "queue" => RefKind.Queue,
                        "gate" => RefKind.Gate,
                        "sink" => RefKind.Sink,
                        _ => throw new InvalidOperationException("unknown kind " + kind),
                    };
                    _kind.Add(id, k);
                    if (k == RefKind.Queue)
                    {
                        _queues.Add(id, new RefQueue
                        {
                            ServerCount = n.GetProperty("server_count").GetInt32(),
                            Open = n.GetProperty("servers_open").GetInt32(),
                            Profile = queueProfiles[n.GetProperty("queue_profile").GetString()!],
                        });
                    }

                    if (k == RefKind.Gate)
                    {
                        _gates.Add(id);
                    }
                }
            }

            _gates.Sort();
        }

        public IEnumerable<uint> Nodes => _kind.Keys;

        public bool IsQueue(uint node)
        {
            return _kind[node] == RefKind.Queue;
        }

        public static string KeyText(in CohortKey k)
        {
            return "f" + k.Flight.Value + "/" + k.Direction + "/" + k.PaxProfile.Value + "/" + (k.HasHoldBaggage ? "b" : "-") + (k.RequiresAssistance ? "a" : "-");
        }

        // ---- inputs, in the order 09 §9.6 pins for tick t ----

        /// <summary>1. A SetServersOpen command applied in phase 1 (§9.8: clamped).</summary>
        public void ApplyServersOpen(uint node, int count)
        {
            RefQueue q = _queues[node];
            q.Open = Math.Max(0, Math.Min(count, q.ServerCount));
            _commandThisTick = true;
        }

        /// <summary>2. The caller's Inject: one new id per call; EnteredNodeAt = N (§9.7).</summary>
        public void Inject(in CohortKey key, int count, uint at)
        {
            _cohorts.Add(new RefCohort { Id = _nextId++, Key = key, Node = at, Count = count, Entered = _ticksDone, Due = _ticksDone });
        }

        /// <summary>2. The caller's Absorb (§9.7 "Absorb, in this order").</summary>
        public int Absorb(uint sink, FlightId flight)
        {
            if (_kind[sink] != RefKind.Sink)
            {
                throw new InvalidOperationException("script absorbs into a non-Sink");
            }

            int boarded = 0;
            int missed = 0;
            var missedAt = new SortedDictionary<uint, int>();
            var removed = new List<RefCohort>();
            foreach (RefCohort c in _cohorts)
            {
                if (c.Key.Flight != flight || c.Key.Direction != FlowDirection.Departing)
                {
                    continue;
                }

                removed.Add(c);
                if (_kind[c.Node] == RefKind.Gate)
                {
                    boarded += c.Count;
                    continue;
                }

                missed += c.Count;
                missedAt[c.Node] = (missedAt.TryGetValue(c.Node, out int m) ? m : 0) + c.Count;
                if (c.Open)
                {
                    // Ascending CohortId, because _cohorts is.
                    Events.Add("unblocked h" + c.Held + " b" + c.By + " k" + KeyText(c.Key));
                    MissedWhileBlocked++;
                }
            }

            if (missed > 0)
            {
                uint most = 0;
                int mostCount = -1;
                foreach (KeyValuePair<uint, int> kv in missedAt)
                {
                    if (kv.Value > mostCount)
                    {
                        most = kv.Key;
                        mostCount = kv.Value;
                    }
                }

                Events.Add("missed f" + flight.Value + " x" + missed + " at n" + most);
            }

            foreach (RefCohort c in removed)
            {
                _cohorts.Remove(c);
            }

            return boarded;
        }

        /// <summary>3. §9.12's Tick for t.</summary>
        public void Tick(ulong t)
        {
            if (t != _ticksDone)
            {
                throw new InvalidOperationException("reference ticks out of order");
            }

            // 1. Snapshot.
            var snapPop = new Dictionary<uint, int>();
            var snapWait = new Dictionary<uint, Fx>();
            foreach (uint n in _kind.Keys)
            {
                snapPop[n] = PopulationOf(n);
            }

            foreach (KeyValuePair<uint, RefQueue> q in _queues)
            {
                snapWait[q.Key] = Wait(snapPop[q.Key], q.Value);
            }

            var routed = new Dictionary<(uint, long), uint>();
            var routedByNode = new Dictionary<uint, List<(long Speed, uint Edge)>>();

            // 2. Movement, nodes ascending.
            foreach (KeyValuePair<uint, RefKind> node in _kind)
            {
                switch (node.Value)
                {
                    case RefKind.Source:
                    case RefKind.Hall:
                    {
                        List<RefCohort> leaving = On(node.Key, c => c.Entered < t);
                        if (node.Value == RefKind.Source && leaving.Count >= 3)
                        {
                            ShowUpSpikes++;
                        }

                        foreach (RefCohort c in leaving)
                        {
                            TryLeave(c, c.Count, t, snapPop, snapWait, routed, routedByNode);
                        }

                        break;
                    }

                    case RefKind.Corridor:
                        foreach (RefCohort c in On(node.Key, c => c.Due <= t))
                        {
                            TryLeave(c, c.Count, t, snapPop, snapWait, routed, routedByNode);
                        }

                        break;

                    case RefKind.Queue:
                        ServeQueue(node.Key, t, snapPop, snapWait, routed, routedByNode);
                        break;

                    default:
                        break;
                }
            }

            foreach (KeyValuePair<uint, List<(long Speed, uint Edge)>> kv in routedByNode)
            {
                var edgeBySpeed = new Dictionary<long, uint>();
                foreach ((long speed, uint edge) in kv.Value)
                {
                    edgeBySpeed[speed] = edge;
                }

                var edges = new HashSet<uint>(edgeBySpeed.Values);
                if (edgeBySpeed.Count >= 2 && edges.Count >= 2)
                {
                    WalkSpeedSplits++;
                }
            }

            if (_commandThisTick && _routedLastTickAt + 1 == t)
            {
                foreach (KeyValuePair<(uint, long), uint> kv in routed)
                {
                    if (_routedLastTick.TryGetValue(kv.Key, out uint before) && before != kv.Value)
                    {
                        LaneChangeFlips++;
                        break;
                    }
                }
            }

            _routedLastTick = routed;
            _routedLastTickAt = t;
            _commandThisTick = false;

            // 3. Merge (§9.3), cohorts in open episodes excluded; survivor lowest id.
            var survivors = new Dictionary<(uint, CohortKey, ulong), RefCohort>();
            for (int i = 0; i < _cohorts.Count;)
            {
                RefCohort c = _cohorts[i];
                if (c.Open)
                {
                    i++;
                    continue;
                }

                bool corridor = _kind[c.Node] == RefKind.Corridor;
                var k = (c.Node, c.Key, corridor ? c.Due : 0UL);
                if (survivors.TryGetValue(k, out RefCohort? s))
                {
                    s.Count += c.Count;
                    s.Entered = Math.Min(s.Entered, c.Entered);
                    if (!corridor)
                    {
                        s.Due = s.Entered;
                    }

                    _cohorts.RemoveAt(i);
                }
                else
                {
                    survivors.Add(k, c);
                    i++;
                }
            }

            // 4. Thresholds, queues ascending, from the post-merge state.
            foreach (KeyValuePair<uint, RefQueue> q in _queues)
            {
                Fx w = Wait(PopulationOf(q.Key), q.Value);
                if (!q.Value.Flag && w > q.Value.Profile.Threshold)
                {
                    q.Value.Flag = true;
                    Events.Add("exceeded n" + q.Key + " w" + w.Raw + " " + q.Value.Open + "/" + q.Value.ServerCount);
                }
                else if (q.Value.Flag && w < Fx.Sub(q.Value.Profile.Threshold, q.Value.Profile.Hysteresis))
                {
                    q.Value.Flag = false;
                    Events.Add("cleared n" + q.Key + " w" + w.Raw + " " + q.Value.Open + "/" + q.Value.ServerCount);
                }
            }

            _ticksDone = t + 1;
        }

        /// <summary>The observable state after a tick, as canonical lines (09 §9.6 "Assertion").</summary>
        public void Observe(List<string> lines)
        {
            foreach (uint n in _kind.Keys)
            {
                lines.Add("n" + n + " pop " + PopulationOf(n));
                var byKey = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (RefCohort c in _cohorts)
                {
                    if (c.Node == n)
                    {
                        string k = KeyText(c.Key);
                        byKey[k] = (byKey.TryGetValue(k, out int v) ? v : 0) + c.Count;
                    }
                }

                foreach (KeyValuePair<string, int> kv in byKey)
                {
                    lines.Add("n" + n + " k" + kv.Key + " x" + kv.Value);
                }

                if (_queues.TryGetValue(n, out RefQueue? q))
                {
                    lines.Add("n" + n + " wait " + Wait(PopulationOf(n), q).Raw);
                }
            }

            lines.AddRange(Events);
            Events.Clear();
        }

        // ---- §9.12 pieces ----

        private void ServeQueue(uint node, ulong t, Dictionary<uint, int> snapPop, Dictionary<uint, Fx> snapWait, Dictionary<(uint, long), uint> routed, Dictionary<uint, List<(long, uint)>> routedByNode)
        {
            RefQueue q = _queues[node];
            Fx rate = q.Profile.Rate;
            int secs = SimConstants.SIM_SECONDS_PER_TICK;
            Fx capacity = Fx.Div(Fx.Mul(Fx.FromInt(q.Open * secs), rate), Fx.FromInt(60));
            Fx serverTick = Fx.Div(Fx.Mul(Fx.FromInt(secs), rate), Fx.FromInt(60));
            q.Credit = Fx.Add(q.Credit, capacity);
            long served = Fx.Floor(q.Credit);
            q.Credit = Fx.Sub(q.Credit, Fx.FromInt(served));

            List<RefCohort> fifo = On(node, c => c.Entered < t);
            fifo.Sort((a, b) => a.Entered != b.Entered ? a.Entered.CompareTo(b.Entered) : a.Id.CompareTo(b.Id));
            long moved = 0;
            foreach (RefCohort c in fifo)
            {
                if (moved >= served)
                {
                    break;
                }

                int take = (int)Math.Min(served - moved, c.Count);
                if (!TryLeave(c, take, t, snapPop, snapWait, routed, routedByNode))
                {
                    break;
                }

                moved += take;
            }

            if (moved < served)
            {
                q.Credit = Fx.Min(q.Credit, serverTick);
            }
        }

        private bool TryLeave(RefCohort c, int take, ulong t, Dictionary<uint, int> snapPop, Dictionary<uint, Fx> snapWait, Dictionary<(uint, long), uint> routed, Dictionary<uint, List<(long, uint)>> routedByNode)
        {
            Fx speed = _speed[c.Key.PaxProfile.Value];
            uint edge = Route(c.Node, speed, snapWait);
            uint target = _world.EdgeTo(new EdgeId(edge)).Value;
            routed[(c.Node, speed.Raw)] = edge;
            if (!routedByNode.TryGetValue(c.Node, out List<(long, uint)>? list))
            {
                list = new List<(long, uint)>();
                routedByNode.Add(c.Node, list);
            }

            list.Add((speed.Raw, edge));

            string key = KeyText(c.Key);
            bool full = _kind[target] == RefKind.Queue && snapPop[target] >= _queues[target].Profile.Capacity;
            if (full)
            {
                Refusals++;
                if (!c.Open)
                {
                    c.Open = true;
                    c.Held = c.Node;
                    c.By = target;
                    Events.Add("blocked h" + c.Held + " b" + c.By + " k" + key);
                }
                else if (c.By != target)
                {
                    Events.Add("unblocked h" + c.Held + " b" + c.By + " k" + key);
                    c.Held = c.Node;
                    c.By = target;
                    Events.Add("blocked h" + c.Held + " b" + c.By + " k" + key);
                    BlockedRerouteReblocked++;
                }

                return false;
            }

            if (c.Open)
            {
                Events.Add("unblocked h" + c.Held + " b" + c.By + " k" + key);
                if (c.By != target)
                {
                    BlockedRerouteMoved++;
                }

                c.Open = false;
            }

            ulong due = _kind[target] == RefKind.Corridor ? t + (ulong)Traversal(target, speed) : t;
            _cohorts.Add(new RefCohort { Id = _nextId++, Key = c.Key, Node = target, Count = take, Entered = t, Due = due });
            if (take == c.Count)
            {
                _cohorts.Remove(c);
            }
            else
            {
                c.Count -= take;
            }

            if (_kind[target] == RefKind.Gate)
            {
                Events.Add("arrived f" + c.Key.Flight.Value + " x" + take);
            }

            return true;
        }

        /// <summary>
        /// §9.6's rule, from scratch: over every (e, g) with CanReachVia(e, g),
        /// the lowest cost by Raw, then ascending g, then ascending EdgeId.
        /// </summary>
        private uint Route(uint node, Fx speed, Dictionary<uint, Fx> snapWait)
        {
            var pairs = new List<(long Cost, uint Gate, uint Edge)>();
            Fx ticksPerMinute = Fx.FromInt((long)SimConstants.TICKS_PER_SIM_MINUTE);
            foreach (EdgeId e in _world.OutEdges(new NodeId(node)))
            {
                foreach (uint g in _gates)
                {
                    if (!_world.CanReachVia(e, new NodeId(g)))
                    {
                        continue;
                    }

                    Fx cost = Fx.Zero;
                    foreach (NodeId p in _world.PathVia(e, new NodeId(g)))
                    {
                        cost = Fx.Add(cost, Fx.FromInt(Traversal(p.Value, speed)));
                        if (_kind[p.Value] == RefKind.Queue)
                        {
                            cost = Fx.Add(cost, Fx.Mul(snapWait[p.Value], ticksPerMinute));
                        }
                    }

                    pairs.Add((cost.Raw, g, e.Value));
                }
            }

            if (pairs.Count == 0)
            {
                throw new InvalidOperationException("fixture: node " + node + " reaches no gate");
            }

            (long Cost, uint Gate, uint Edge) best = pairs[0];
            foreach (var p in pairs)
            {
                if (p.Cost < best.Cost || (p.Cost == best.Cost && (p.Gate < best.Gate || (p.Gate == best.Gate && p.Edge < best.Edge))))
                {
                    best = p;
                }
            }

            foreach (var p in pairs)
            {
                if (p.Cost != best.Cost)
                {
                    continue;
                }

                if (p.Gate > best.Gate && p.Edge < best.Edge)
                {
                    GateTieBreaks++;
                }

                if (p.Gate == best.Gate && p.Edge > best.Edge && _world.EdgeTo(new EdgeId(p.Edge)) != _world.EdgeTo(new EdgeId(best.Edge)))
                {
                    EdgeTieBreaks++;
                }
            }

            return best.Edge;
        }

        /// <summary>§9.12 "Traversal and route cost", per node, never from a summed length.</summary>
        private long Traversal(uint node, Fx speed)
        {
            Fx length = Fx.FromInt(_world.LengthMetres(new NodeId(node)));
            return Math.Max(1, Fx.Ceil(Fx.Div(length, Fx.Mul(speed, Fx.FromInt(SimConstants.SIM_SECONDS_PER_TICK)))));
        }

        private static Fx Wait(int population, RefQueue q)
        {
            Fx perMinute = Fx.Mul(Fx.FromInt(q.Open), q.Profile.Rate);
            return Fx.Div(Fx.FromInt(population), Fx.Max(perMinute, Epsilon));
        }

        private int PopulationOf(uint node)
        {
            int p = 0;
            foreach (RefCohort c in _cohorts)
            {
                if (c.Node == node)
                {
                    p += c.Count;
                }
            }

            return p;
        }

        private List<RefCohort> On(uint node, Func<RefCohort, bool> eligible)
        {
            var list = new List<RefCohort>();
            foreach (RefCohort c in _cohorts)
            {
                if (c.Node == node && eligible(c))
                {
                    list.Add(c);
                }
            }

            return list;
        }
    }
}
