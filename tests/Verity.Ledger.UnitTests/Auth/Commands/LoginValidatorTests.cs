using FluentAssertions;
using Verity.Ledger.Application.Auth.Commands.Login;

namespace Verity.Ledger.UnitTests.Auth.Commands;

public class LoginValidatorTests
{
    private readonly LoginValidator _validator = new();

    [Fact]
    public void Comando_valido_passa_na_validacao()
    {
        var result = _validator.Validate(new LoginCommand("comerciante", "senha-123"));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "senha")]
    [InlineData("usuario", "")]
    public void Username_ou_password_vazio_falha_na_validacao(string username, string password)
    {
        var result = _validator.Validate(new LoginCommand(username, password));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Username_acima_de_128_caracteres_falha_na_validacao()
    {
        var result = _validator.Validate(new LoginCommand(new string('a', 129), "senha-123"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(LoginCommand.Username));
    }

    [Fact]
    public void Password_acima_de_72_caracteres_falha_na_validacao()
    {
        // BCrypt ignora bytes além de 72 — permitir mais que isso no request daria uma falsa
        // sensação de entropia adicional na senha (ver LoginValidator).
        var result = _validator.Validate(new LoginCommand("comerciante", new string('a', 73)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(LoginCommand.Password));
    }
}
