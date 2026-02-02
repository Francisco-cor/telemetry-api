using System;
using Xunit;
using Telemetry.Api.Infra;

namespace Telemetry.Api.Tests;

public class SequentialGuidGeneratorTests
{
    [Fact]
    public void Create_ShouldReturnValidGuid()
    {
        // Act
        var guid = SequentialGuidGenerator.Create();

        // Assert
        Assert.NotEqual(Guid.Empty, guid);
    }

    [Fact]
    public void Create_ShouldProduceSequentialGuids()
    {
        // Act
        var guid1 = SequentialGuidGenerator.Create();
        System.Threading.Thread.Sleep(10); // Ensure timestamp changes
        var guid2 = SequentialGuidGenerator.Create();

        // Assert
        // Sequential GUIDs starting with timestamp should be sortable
        Assert.True(guid1.CompareTo(guid2) < 0);
    }
}
