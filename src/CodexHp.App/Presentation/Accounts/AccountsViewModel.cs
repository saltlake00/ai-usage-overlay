using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodexHp.App.Accounts;

namespace CodexHp.App.Presentation.Accounts;

/// <summary>
/// 계정 연동 화면의 ViewModel. 비밀은 설정 모델에 바인딩하지 않고
/// 연결 실행 시에만 전달하며, 연결 종료·취소·창 닫힘 때 입력을 비운다.
/// </summary>
public sealed class AccountsViewModel : INotifyPropertyChanged
{
    private readonly AccountConnectionService service;
    private readonly Dictionary<string, bool> connecting;
    private readonly CancellationTokenSource lifetimeCancellation = new();

    public AccountsViewModel(AccountConnectionService service)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.connecting = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action? CloseRequested;

    public IReadOnlyList<string> ProviderIds { get; } = ["codex", "claude", "ollama"];

    public ConnectionStatus GetStatus(string providerId) => this.service.GetState(providerId).Status;

    public bool IsConnecting(string providerId) =>
        this.connecting.TryGetValue(providerId, out var value) && value;

    public async Task ConnectAsync(string providerId, string? secret, CancellationToken cancellationToken)
    {
        if (this.IsConnecting(providerId))
        {
            return;
        }

        this.SetConnecting(providerId, true);
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                this.lifetimeCancellation.Token);
            await this.service.ConnectAsync(providerId, secret, linked.Token);
        }
        finally
        {
            this.SetConnecting(providerId, false);
        }
    }

    public async Task DisconnectAsync(string providerId, CancellationToken cancellationToken)
    {
        if (this.IsConnecting(providerId))
        {
            return;
        }

        await this.service.DisconnectAsync(providerId, cancellationToken);
        this.OnPropertyChanged(string.Empty);
    }

    public void Close()
    {
        this.lifetimeCancellation.Cancel();
        this.CloseRequested?.Invoke();
    }

    private void SetConnecting(string providerId, bool value)
    {
        this.connecting[providerId] = value;
        this.OnPropertyChanged(nameof(IsConnecting));
        this.OnPropertyChanged(string.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
