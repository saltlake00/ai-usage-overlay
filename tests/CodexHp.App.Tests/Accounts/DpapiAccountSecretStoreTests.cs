using CodexHp.App.Accounts;
using Xunit;

namespace CodexHp.App.Tests.Accounts;

public sealed class DpapiAccountSecretStoreTests
{
    [Fact]
    public void RoundTripAndDelete()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DpapiAccountSecretStore(dir);
            store.Write("claude", "synthetic-test-secret");
            Assert.Equal("synthetic-test-secret", store.Read("claude"));
            store.Delete("claude");
            Assert.Null(store.Read("claude"));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void Write_overwrites_an_existing_secret()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DpapiAccountSecretStore(dir);
            store.Write("claude", "first");
            store.Write("claude", "second");
            Assert.Equal("second", store.Read("claude"));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void Delete_missing_provider_is_a_noop()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DpapiAccountSecretStore(dir);
            store.Delete("claude");
            Assert.Null(store.Read("claude"));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void Read_corrupted_ciphertext_returns_null()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "claude.bin"), "not-valid-ciphertext");
            var store = new DpapiAccountSecretStore(dir);
            Assert.Null(store.Read("claude"));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void Write_rejects_provider_ids_outside_the_allow_list()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DpapiAccountSecretStore(dir);
            Assert.Throws<ArgumentOutOfRangeException>(() => store.Write("unknown", "secret"));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void Write_does_not_leave_a_plaintext_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DpapiAccountSecretStore(dir);
            store.Write("claude", "synthetic-test-secret");
            var files = Directory.GetFiles(dir);
            Assert.All(files, file => Assert.DoesNotContain("synthetic-test-secret", File.ReadAllText(file)));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
