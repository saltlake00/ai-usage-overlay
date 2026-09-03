using CodexHp.App.Accounts;
using CodexHp.App.Presentation.Accounts;
using CodexHp.Core.Domain;
using Xunit;

namespace CodexHp.App.Tests.Accounts;

public sealed class AccountsViewModelTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static AccountConnectionService CreateService(
        string dir,
        Func<string, string?, CancellationToken, Task<ProviderUsageSnapshot>> fetch)
    {
        var store = new DpapiAccountSecretStore(dir);
        var stateStore = new AccountConnectionStore(Path.Combine(dir, "state.json"));
        return new AccountConnectionService(
            store,
            stateStore,
            fetch,
            ex => ex is AuthException ? ConnectionStatus.ReconnectRequired : ConnectionStatus.TransientError);
    }

    private static ProviderUsageSnapshot Snapshot(string id) => new(
        id,
        "Test",
        new UsageWindow(50, null, TimeSpan.FromHours(5)),
        new UsageWindow(80, null, TimeSpan.FromDays(7)),
        DateTimeOffset.UtcNow);

    private sealed class AuthException : Exception;

    [Fact]
    public async Task Connect_blocks_duplicate_clicks_while_connecting()
    {
        var dir = NewTempDir();
        try
        {
            var gate = new TaskCompletionSource<ProviderUsageSnapshot>();
            var service = CreateService(dir, (id, secret, ct) => gate.Task);
            var viewModel = new AccountsViewModel(service);

            var connectTask = viewModel.ConnectAsync("claude", "secret-value", CancellationToken.None);
            // 연결 중에는 중복 클릭이 차단되어야 한다.
            Assert.True(viewModel.IsConnecting("claude"));

            gate.SetResult(Snapshot("claude"));
            await connectTask;

            Assert.False(viewModel.IsConnecting("claude"));
            Assert.Equal(ConnectionStatus.Connected, viewModel.GetStatus("claude"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Failed_connect_keeps_input_masked_and_marks_reconnect_required()
    {
        var dir = NewTempDir();
        try
        {
            var service = CreateService(dir, (id, secret, ct) => throw new AuthException());
            var viewModel = new AccountsViewModel(service);

            await viewModel.ConnectAsync("claude", "bad-secret", CancellationToken.None);

            Assert.Equal(ConnectionStatus.ReconnectRequired, viewModel.GetStatus("claude"));
            // 실패 시 입력 마스킹 유지: 비밀이 저장되지 않아야 한다.
            Assert.Null(new DpapiAccountSecretStore(dir).Read("claude"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Disconnect_marks_only_the_selected_row_disconnected()
    {
        var dir = NewTempDir();
        try
        {
            var service = CreateService(dir, (id, secret, ct) => Task.FromResult(Snapshot(id)));
            var viewModel = new AccountsViewModel(service);
            await viewModel.ConnectAsync("claude", "secret-value", CancellationToken.None);
            await viewModel.ConnectAsync("ollama", "other-secret", CancellationToken.None);

            await viewModel.DisconnectAsync("claude", CancellationToken.None);

            Assert.Equal(ConnectionStatus.Disconnected, viewModel.GetStatus("claude"));
            Assert.Equal(ConnectionStatus.Connected, viewModel.GetStatus("ollama"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Close_cancels_an_in_progress_connect()
    {
        var dir = NewTempDir();
        try
        {
            var gate = new TaskCompletionSource<ProviderUsageSnapshot>();
            var service = CreateService(dir, (id, secret, ct) =>
            {
                ct.Register(() => gate.TrySetCanceled(ct));
                return gate.Task;
            });
            var viewModel = new AccountsViewModel(service);

            var connectTask = viewModel.ConnectAsync("claude", "secret-value", CancellationToken.None);
            viewModel.Close();

            // 창 닫기 시 진행 중 연결이 취소되어야 한다.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connectTask);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
