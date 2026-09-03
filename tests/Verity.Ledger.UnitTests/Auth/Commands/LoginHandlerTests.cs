using FluentAssertions;
using NSubstitute;
using Verity.Ledger.Application.Abstractions;
using Verity.Ledger.Application.Auth.Commands.Login;
using Verity.Ledger.Domain.Users;

namespace Verity.Ledger.UnitTests.Auth.Commands;

public class LoginHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly LoginHandler _handler;

    public LoginHandlerTests()
    {
        _handler = new LoginHandler(_users, _passwordHasher);
    }

    [Fact]
    public async Task Credenciais_validas_retornam_o_usuario_autenticado()
    {
        var user = User.Register("comerciante", "hash-armazenado", "Comerciante");
        _users.GetByUsernameAsync("comerciante", Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify("senha-correta", "hash-armazenado").Returns(true);

        var result = await _handler.HandleAsync(new LoginCommand("comerciante", "senha-correta"), CancellationToken.None);

        result.Should().NotBeNull();
        result!.UserId.Should().Be(user.Id);
        result.Username.Should().Be("comerciante");
        result.DisplayName.Should().Be("Comerciante");
    }

    [Fact]
    public async Task Senha_incorreta_retorna_null()
    {
        var user = User.Register("comerciante", "hash-armazenado", "Comerciante");
        _users.GetByUsernameAsync("comerciante", Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify("senha-errada", "hash-armazenado").Returns(false);

        var result = await _handler.HandleAsync(new LoginCommand("comerciante", "senha-errada"), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Usuario_inexistente_retorna_null_sem_chamar_o_verificador_de_senha()
    {
        _users.GetByUsernameAsync("fulano", Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _handler.HandleAsync(new LoginCommand("fulano", "qualquer"), CancellationToken.None);

        result.Should().BeNull();
        _passwordHasher.DidNotReceiveWithAnyArgs().Verify(default!, default!);
    }
}
