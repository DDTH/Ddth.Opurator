# Ddth.Opurator release notes

## 2026-10-06 - v0.1.0

### Added/Refactoring/Deprecation

- Feat(execution): add bounded in-process background task execution
- Feat(scheduling): support delayed one-shot tasks and configurable repeat schedules
- Feat(repeat): prevent overlapping runs with configurable failure handling
- Feat(observability): expose typed results, task snapshots, and run history
- Feat(lifecycle): add per-run timeouts, cancellation, and graceful shutdown
- Feat(di): add IServiceCollection registration for IBackgroundTaskManager
