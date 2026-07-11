using Xunit;

// SQLite connection pools are process-global, and TestDatabase.Dispose() calls
// SqliteConnection.ClearAllPools() to release the file handle before deleting its temp database.
// Under xunit's default parallelism, one finishing test's pool-clear can drop another test's
// in-flight pooled connection, causing intermittent failures. These tests are fast, so run the
// whole assembly sequentially to keep them deterministic.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
