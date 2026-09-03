using CodexHp.Core.Domain;

namespace CodexHp.App.Accounts;

/// <summary>
/// 공급자별 계정 연결 상태와 조회 수명주기를 관리한다.
/// 연결 흐름: 확인 중 → 입력값으로 조회 → 성공한 비밀만 저장 → 연결됨.
/// 해제 흐름: 비활성 상태 저장 → 세대 증가 → 앱 비밀 삭제.
/// 세대 번호가 바뀌면 이전 세대의 늦은 조회 결과는 폐기한다.
/// </summary>
public sealed class AccountConnectionService
{
    private readonly IAccountSecretStore secretStore;
    private readonly AccountConnectionStore stateStore;
    private readonly Func<string, string?, CancellationToken, Task<ProviderUsageSnapshot>> fetch;
    private readonly Func<Exception, ConnectionStatus> classify;
    private readonly Dictionary<string, AccountConnectionState> states;
    private readonly Dictionary<string, long> generations;
    private readonly object sync = new();

    public AccountConnectionService(
        IAccountSecretStore secretStore,
        AccountConnectionStore stateStore,
        Func<string, string?, CancellationToken, Task<ProviderUsageSnapshot>> fetch,
        Func<Exception, ConnectionStatus> classify)
    {
        this.secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        this.stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        this.fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
        this.classify = classify ?? throw new ArgumentNullException(nameof(classify));
        this.states = new Dictionary<string, AccountConnectionState>(StringComparer.OrdinalIgnoreCase);
        this.generations = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var (providerId, state) in this.stateStore.Load())
        {
            this.states[providerId] = state;
            this.generations[providerId] = state.Generation;
        }
    }

    public async Task<AccountConnectionState> ConnectAsync(
        string providerId,
        string? secret,
        CancellationToken cancellationToken)
    {
        long generation;
        lock (this.sync)
        {
            generation = this.NextGeneration(providerId);
            this.states[providerId] = new AccountConnectionState(
                providerId,
                ConnectionStatus.Connecting,
                generation);
            this.SaveLocked();
        }

        try
        {
            await this.fetch(providerId, secret, cancellationToken);
            lock (this.sync)
            {
                if (this.generations[providerId] != generation)
                {
                    // 이전 세대의 늦은 응답 — 폐기하고 현재 상태를 반환한다.
                    return this.states[providerId];
                }

                if (secret is not null)
                {
                    this.secretStore.Write(providerId, secret);
                }

                this.states[providerId] = new AccountConnectionState(
                    providerId,
                    ConnectionStatus.Connected,
                    generation);
                this.SaveLocked();
                return this.states[providerId];
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var status = this.classify(exception);
            lock (this.sync)
            {
                if (this.generations[providerId] != generation)
                {
                    return this.states[providerId];
                }

                this.states[providerId] = new AccountConnectionState(
                    providerId,
                    status,
                    generation);
                this.SaveLocked();
                return this.states[providerId];
            }
        }
    }

    public Task DisconnectAsync(string providerId, CancellationToken cancellationToken)
    {
        lock (this.sync)
        {
            var generation = this.NextGeneration(providerId);
            this.states[providerId] = new AccountConnectionState(
                providerId,
                ConnectionStatus.Disconnected,
                generation);
            this.SaveLocked();
        }

        this.secretStore.Delete(providerId);
        return Task.CompletedTask;
    }

    public AccountConnectionState GetState(string providerId)
    {
        lock (this.sync)
        {
            return this.states.TryGetValue(providerId, out var state)
                ? state
                : new AccountConnectionState(providerId, ConnectionStatus.Disconnected, 0);
        }
    }

    /// <summary>
    /// 저장된 비밀을 읽어 주기 조회를 수행한다. 연결되지 않았거나 인증 만료 상태면 조회하지 않는다.
    /// 반환값은 (성공 여부, 스냅샷, 현재 세대)다. 호출자는 캐시/화면 반영 직전에 세대를 다시 검사해야 한다.
    /// </summary>
    public async Task<(bool Success, ProviderUsageSnapshot? Snapshot, long Generation)> FetchAsync(
        string providerId,
        CancellationToken cancellationToken)
    {
        AccountConnectionState state;
        string? secret;
        long generation;
        lock (this.sync)
        {
            state = this.GetState(providerId);
            if (state.Status is not (ConnectionStatus.Connected or ConnectionStatus.TransientError))
            {
                return (false, null, state.Generation);
            }

            generation = state.Generation;
        }

        try
        {
            secret = this.secretStore.Read(providerId);
        }
        catch
        {
            return (false, null, generation);
        }

        if (secret is null)
        {
            return (false, null, generation);
        }

        try
        {
            var snapshot = await this.fetch(providerId, secret, cancellationToken);
            lock (this.sync)
            {
                if (this.generations[providerId] != generation)
                {
                    // 이전 세대의 늦은 응답 — 폐기한다.
                    return (false, null, this.generations[providerId]);
                }

                this.states[providerId] = new AccountConnectionState(
                    providerId,
                    ConnectionStatus.Connected,
                    generation);
                this.SaveLocked();
                return (true, snapshot, generation);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var status = this.classify(exception);
            lock (this.sync)
            {
                if (this.generations[providerId] != generation)
                {
                    return (false, null, this.generations[providerId]);
                }

                this.states[providerId] = new AccountConnectionState(
                    providerId,
                    status,
                    generation);
                this.SaveLocked();
                return (false, null, generation);
            }
        }
    }

    private long NextGeneration(string providerId)
    {
        var next = this.generations.GetValueOrDefault(providerId) + 1;
        this.generations[providerId] = next;
        return next;
    }

    private void SaveLocked() => this.stateStore.Save(this.states);
}
