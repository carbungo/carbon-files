using CarbonFiles.Client.Events;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Client.Tests.Events;

public class CarbonFilesEventsTests
{
    [Fact]
    public void Construction_DoesNotThrow()
    {
        var events = new CarbonFilesEvents(new Uri("https://example.com"), "test-key");
        events.Should().NotBeNull();
    }

    [Fact]
    public void OnFileCreated_RegistersHandler()
    {
        var events = new CarbonFilesEvents(new Uri("https://example.com"), "test-key");
        var called = false;
        events.OnFileCreated((bucketId, file) => { called = true; return Task.CompletedTask; });
        called.Should().BeFalse(); // not called yet, just registered
    }

    [Fact]
    public void AllEventHandlers_CanBeRegistered()
    {
        var events = new CarbonFilesEvents(new Uri("https://example.com"), "test-key");
        events.OnFileCreated((id, file) => Task.CompletedTask);
        events.OnFileUpdated((id, file) => Task.CompletedTask);
        events.OnFileDeleted((id, path) => Task.CompletedTask);
        events.OnBucketCreated(bucket => Task.CompletedTask);
        events.OnBucketUpdated((id, changes) => Task.CompletedTask);
        events.OnBucketDeleted(id => Task.CompletedTask);
    }

    [Fact]
    public void Client_Events_IsAccessible()
    {
        var client = new CarbonFiles.Client.CarbonFilesClient("https://example.com", "test-key");
        client.Events.Should().NotBeNull();
    }
}
