using System;
using System.Threading;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class SingleInstanceServiceTests
{
    [Fact]
    public void TryAcquire_ShouldReturnTrue_WhenFirstInvocation()
    {
        var name = $"DropSendTo.Tests.{Guid.NewGuid()}";
        using var service = new SingleInstanceService(name);

        bool acquired = service.TryAcquire();

        acquired.Should().BeTrue();
    }

    [Fact]
    public void TryAcquire_ShouldReturnFalse_WhenMutexAlreadyOwned()
    {
        var name = $"DropSendTo.Tests.{Guid.NewGuid()}";
        using var first = new SingleInstanceService(name);
        first.TryAcquire().Should().BeTrue();

        bool acquired = false;
        var secondThread = new Thread(() =>
        {
            using var second = new SingleInstanceService(name);
            acquired = second.TryAcquire();
        })
        {
            IsBackground = true
        };

        secondThread.Start();
        secondThread.Join();

        acquired.Should().BeFalse();
    }
}
