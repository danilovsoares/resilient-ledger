using FluentAssertions;
using Verity.Ledger.Domain.Exceptions;
using Verity.Ledger.Domain.Users;

namespace Verity.Ledger.UnitTests.Users;

public class UserTests
{
    [Fact]
    public void Register_com_dados_validos_normaliza_username_para_minusculo()
    {
        var user = User.Register(" Comerciante ", "hash-opaco", "Comerciante");

        user.Id.Should().NotBeEmpty();
        user.Username.Should().Be("comerciante");
        user.PasswordHash.Should().Be("hash-opaco");
        user.DisplayName.Should().Be("Comerciante");
    }

    [Fact]
    public void Register_sem_display_name_usa_o_username_como_fallback()
    {
        var user = User.Register("comerciante", "hash-opaco", displayName: "");

        user.DisplayName.Should().Be("comerciante");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_sem_username_lanca_DomainException(string username)
    {
        var act = () => User.Register(username, "hash-opaco", "Nome");

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_sem_hash_de_senha_lanca_DomainException(string passwordHash)
    {
        var act = () => User.Register("comerciante", passwordHash, "Nome");

        act.Should().Throw<DomainException>();
    }
}
