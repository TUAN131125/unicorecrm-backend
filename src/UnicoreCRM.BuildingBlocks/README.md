# Building Blocks

Only proven, owner-neutral technical primitives may be added here. Business models and speculative
generic infrastructure are forbidden.

Current contents:

- `DevelopmentSchemaMigration` — the registration primitive that lets an owner declare how its own
  schema is migrated during local Development startup. It carries no business concept, owns no
  persistence and runs nothing by itself; the composition root decides whether the registered
  owner callbacks run.
