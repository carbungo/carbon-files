using CarbonFiles.Core.Configuration;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CarbonFiles.Infrastructure.Tests.Services;

public sealed class BucketActivityServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly BucketActivityService _sut;

    public BucketActivityServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"cf_bucket_activity_{Guid.NewGuid():N}.db");
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        DatabaseInitializer.Initialize(_connection);

        var options = Options.Create(new CarbonFilesOptions
        {
            DbPath = _dbPath,
            BucketActivityIntervalSeconds = 5
        });
        _sut = new BucketActivityService(options, NullLogger<BucketActivityService>.Instance);

        InsertBucket("bucket-one");
        InsertBucket("bucket-two");
        InsertBucket("untouched");
    }

    [Fact]
    public async Task Touch_DeduplicatesIds_AndDrainUpdatesEachTouchedBucket()
    {
        _sut.Touch("bucket-one");
        _sut.Touch("bucket-one");
        _sut.Touch("bucket-two");

        var drained = await _sut.DrainAsync(TestContext.Current.CancellationToken);

        drained.Should().BeEquivalentTo(["bucket-one", "bucket-two"]);
        ReadLastUsedAt("bucket-one").Should().NotBeNull();
        ReadLastUsedAt("bucket-two").Should().NotBeNull();
        ReadLastUsedAt("untouched").Should().BeNull();

        var secondDrain = await _sut.DrainAsync(TestContext.Current.CancellationToken);
        secondDrain.Should().BeEmpty();
    }

    public void Dispose()
    {
        _sut.Dispose();
        _connection.Dispose();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private void InsertBucket(string id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "INSERT INTO Buckets (Id, Name, Owner, CreatedAt) VALUES (@id, @name, @owner, @createdAt)";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@name", id);
        command.Parameters.AddWithValue("@owner", "test-owner");
        command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow);
        command.ExecuteNonQuery();
    }

    private DateTime? ReadLastUsedAt(string id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT LastUsedAt FROM Buckets WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToDateTime(value);
    }
}
