# Content loader fixtures (T-027)

Synthetic `data/`-shaped trees for the `sim.core` content loader tests
(`08-interfaces-core.md` §8.11, `04-data-schemas.md`). These are test
fixtures, not content: no value here is a balance value, and nothing here is
read by the game. Written by the Test Author (`07` L8, Q-021).

- `valid/` loads successfully. `valid.files` lists its files, one relative
  path per line, and is the only list the tests use, so the fixture never
  depends on directory enumeration or on a case-insensitive file system.
- Besides the four kind directories, `valid/` holds files the loader must
  ignore: other directories (`schemas/`, `policies/`, `balance/`), a
  near-miss directory name (`size_categories_old/`) and a non-`.json` file
  in a kind directory. File names make ordinal and culture-aware path
  orders differ (`B777` < `a10` < `a2` < `a320`).

Every failure case is the `valid/` tree with one file replaced or added,
built in memory by `LoaderTestKit`. So is the case-variant directory
(`Aircraft/`), which a case-insensitive file system cannot hold beside
`aircraft/`.
