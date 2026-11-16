# Changelog

## [0.1.0] - 2026-11-15

### Added

- `InMemoryKnowledgeStore`: a lock-guarded, deterministic `IKnowledgeStore` for tests and local development. It runs the store contract every provider shares.
- A get or a delete of an id outside the source-id grammar answers `null` or `false` without reading the store, as the durable provider does.
