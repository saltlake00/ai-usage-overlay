using CodexHp.App.Accounts;
using CodexHp.Core.Domain;
using Xunit;

namespace CodexHp.App.Tests.Accounts;

public sealed class AccountConnectionServiceTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static AccountConnectionService CreateService(
        string dir,
        Func<string, string?, CancellationToken, Task<ProviderUsageSnapshot>> fetch,
        Func<Exception, ConnectionStatus>? classify = null,
        IAccountSecretStore? secretStore = null)
    {
        var store = secretStore ?? new DpapiAccountSecretStore(dir);
        var stateStore = new AccountConnectionStore(Path.Combine(dir, "state.json"));
        return new AccountConnectionService(
            store,
            stateStore,
            fetch,
            classify ?? (ex => ex is AuthException ? ConnectionStatus.ReconnectRequired : ConnectionStatus.TransientError));
    }

    private static ProviderUsageSnapshot Snapshot(string id) => new(
        id,
        "Test",
        new UsageWindow(50, null, TimeSpan.FromHours(5)),
        new UsageWindow(80, null, TimeSpan.FromDays(7)),
        DateTimeOffset.UtcNow);

    private sealed class AuthException : Exception;

    [Fact]
    public async Task Connect_succeeds_and_persists_secret_and_connected_state()
    {
        var dir = NewTempDir();
        try
        {
            var service = CreateService(dir, (id, secret, ct) => Task.FromResult(Snapshot(id)));

            var state = await service.ConnectAsync("claude", "secret-value", CancellationToken.None);

            Assert.Equal(ConnectionStatus.Connected, state.Status);
            Assert.Equal(1, state.Generation);
            Assert.Equal("secret-value", new DpapiAccountSecretStore(dir).Read("claude"));
            Assert.Equal(ConnectionStatus.Connected, service.GetState("claude").Status);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task GetState_restores_connected_state_on_restart()
    {
        var dir = NewTempDir();
        try
        {
            var service = CreateService(dir, (id, secret, ct) => Task.FromResult(Snapshot(id)));
            await service.ConnectAsync("claude", "secret-value", CancellationToken.None);

            // 재실행: 새 서비스 인스턴스가 같은 상태 저장소를 읽는다.
            var restarted = CreateService(dir, (id, secret, ct) => Task.FromResult(Snapshot(id)));

            Assert.Equal(ConnectionStatus.Connected, restarted.GetState("claude").Status);
            Assert.Equal(1, restarted.GetState("claude").Generation);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Connect_auth_failure_does_not_persist_secret_and_marks_reconnect_required()
    {
        var dir = NewTempDir();
        try
        {
            var service = CreateService(dir, (id, secret, ct) => throw new AuthException());

            var state = await service.ConnectAsync("claude", "bad-secret", CancellationToken.None);

            Assert.Equal(ConnectionStatus.ReconnectRequired, state.Status);
            Assert.Null(new DpapiAccountSecretStore(dir).Read("claude"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Disconnect_marks_inactive_and_prevents_environment_reconnect()
    {
        var dir = NewTempDir();
        try
        {
            var service = CreateService(dir, (id, secret, ct) => Task.FromResult(Snapshot(id)));
            await service.ConnectAsync("claude", "secret-value", CancellationToken.None);

            await service.DisconnectAsync("claude", CancellationToken.None);

            Assert.Equal(ConnectionStatus.Disconnected, service.GetState("claude").Status);
            Assert.Null(new DpapiAccountSecretStore(dir).Read("claude"));
            // 세대 증가 확인
            Assert.Equal(2, service.GetState("claude").Generation);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Reconnect_discards_stale_response_from_previous_generation()
    {
        var dir = NewTempDir();
        try
        {
            var slowFirst = new TaskCompletionSource<ProviderUsageSnapshot>();
            var service = CreateService(dir, (id, secret, ct) => slowFirst.Task);

            // 첫 연결이 느리게 진행되는 동안 계정을 교체한다.
            var firstConnect = service.ConnectAsync("claude", "first-secret", CancellationToken.None);
            await service.DisconnectAsync("claude", CancellationToken.None);

            // 이전 세대의 늦은 응답이 도착해도 상태를 덮어쓰지 않아야 한다.
            slowFirst.SetResult(Snapshot("claude"));
            await firstConnect;

            Assert.Equal(ConnectionStatus.Disconnected, service.GetState("claude").Status);
            Assert.Equal(2, service.GetState("claude").Generation);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
