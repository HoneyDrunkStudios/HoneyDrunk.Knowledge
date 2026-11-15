# HoneyDrunk.Knowledge agent instructions

HoneyDrunk.Knowledge is the knowledge node of the Grid (ADR-0021): it splits documents into passages, embeds them through the host's `IEmbeddingGenerator`, stores versioned sources through a provider and retrieves ranked passages. Read [README.md](README.md) and each package's README before editing; they describe what the code does now. Version 0.1.0 is not published until a release is cut by the owner; an open PR is not merged behavior.

Read the [shared engineering conventions](https://github.com/HoneyDrunkStudios/HoneyDrunk.Standards/blob/main/HoneyDrunk.Standards/docs/CONVENTIONS.md) and this repository's owning documentation before editing. Apply the parts relevant to this stack; preserve existing public contracts, dependency direction and repository-specific behavior. Verify shared capabilities in current code before reusing them.

## Solution and packages

`HoneyDrunk.Knowledge.slnx` holds four packages under `src/` and their tests under `tests/`:

| Package | Rule |
|---|---|
| `HoneyDrunk.Knowledge.Abstractions` | The contracts. References `HoneyDrunk.AI.Abstractions` and no other HoneyDrunk package. Its surfaces (`IKnowledgeStore`, `IDocumentIngester`, `IRetrievalPipeline`, `KnowledgeSource`) are frozen by the contract-shape canary; a change needs a version bump |
| `HoneyDrunk.Knowledge` | The runtime. References `HoneyDrunk.Knowledge.Abstractions`, `HoneyDrunk.Kernel.Abstractions` and `HoneyDrunk.AI.Abstractions`, and no other package besides the build-only `HoneyDrunk.Standards` analyzers every package uses |
| `HoneyDrunk.Knowledge.Providers.InMemory` | Test and local-development store. References Abstractions only |
| `HoneyDrunk.Knowledge.Providers.Data` | Durable store. References Abstractions and `HoneyDrunk.Data.EntityFramework` (with EF Core); no database driver |

Provider rule: providers reference Abstractions and, for Data, HoneyDrunk.Data. They never reference the runtime or a database driver. Downstream nodes reference `HoneyDrunk.Knowledge.Abstractions` only. The two providers compile `src/Shared/KnowledgeStoreRules.cs` into their own assemblies instead of exposing a public helper, and both run `tests/Shared/KnowledgeStoreContract.cs`.

Every package directory keeps a `README.md` and a `CHANGELOG.md`. Every public member has XML documentation; Release builds have 0 warnings and add no analyzer suppression.

## Build and test

```bash
dotnet test HoneyDrunk.Knowledge.slnx -c Release
dotnet pack HoneyDrunk.Knowledge.slnx -c Release -o artifacts
```

`dotnet test` runs the contracts, the canary (Kernel and AI), the runtime, the in-memory provider and the Data provider on SQLite. It needs no database. `dotnet pack` produces the four `.nupkg` files.

### SQL Server lane

The SQL Server tests are not in the solution. They deploy the three shipped scripts (source folder `src/HoneyDrunk.Knowledge.Providers.Data/Sql/`, packed into the package's `sql/` folder) to a new database on the server named by `HONEYDRUNK_KNOWLEDGE_TEST_SQLSERVER` (a connection string with no database), run the shared store contract and the schema checks, and drop the database:

```bash
sqllocaldb create KnowledgeProviderTests -s
HONEYDRUNK_KNOWLEDGE_TEST_SQLSERVER='Server=(localdb)\KnowledgeProviderTests;Integrated Security=true;Encrypt=false' dotnet test tests/HoneyDrunk.Knowledge.Providers.Data.SqlServerTests -c Release
```

The `SQL Server (Data provider)` workflow runs them on pull requests that touch the provider; it is not a required check. Run the lane when you change the Data provider, the shared rules or the scripts, and report the result; do not describe it as run when it was not.

## Invariants to keep

- No document text, passage text, query text or vector in a log, an activity, a telemetry tag or an exception message the node writes. Record `ToString()` prints lengths, counts and model ids only; never format an `Embedding`.
- Every source records its embedding model, and no score is computed across models.
- Every retrieved chunk carries its source (identity, version) and position.
- The shipped table scripts and the Entity Framework mapping describe the same schema; a change to one changes the other and the schema checks.
- Every `catch (Exception x)` has a `when` filter, and filtering loops use `Where` and `Select`.
- The publish workflow lists all four package IDs in its three package lists. Do not push a tag, change trusted publishing or touch a ruleset as part of an ordinary change.

Work within the selected request. Preserve unrelated changes and use a separate worktree when needed. Review the final diff, use Conventional Commits and ready-for-review PRs with exactly one accurate `Authorship:` line and a `Request:` line; include the authorship in commit trailers. Run meaningful checks for the affected behavior and report the reviewed/tested revision, failures and unrun checks. Preserve required checks and inspect actual latest-head Sonar new-code findings where analysis applies; do not suppress findings or weaken gates to obtain a pass. Legacy Grid Review is retired; do not restore its workers, queues or bypass labels. A configured replacement reviewer is not evidence of a completed review or enforcing merge check.

## Code Review Rules

Apply the [shared review criteria](https://github.com/HoneyDrunkStudios/HoneyDrunk.Standards/blob/main/HoneyDrunk.Standards/docs/CONVENTIONS.md#code-review) to changed behavior, using the repository boundaries above. Report actionable findings with the failing path, concrete impact and a small corrective action; disclose unavailable evidence. These rules grant no cross-repository access or merge authority.

- Review against the selected responsibility and the tracked files. Check dependency direction (the package table above) and that the frozen contracts did not change without a version bump.
- Trace trust and tenant boundaries, failure, retry and cancellation, and bounded work for the behavior added. Keep product policy with its selected owner: this node stores and retrieves passages, it does not decide what a host ingests.
- Require tests that fit the change: the shared store contract for any provider change, the SQL Server lane for Data provider changes, and a content-leak check for anything that logs or tags.
