using Verity.Ledger.Application.Abstractions;
using Verity.Ledger.Application.Common;

namespace Verity.Ledger.Application.Auth.Commands.Login;

/// <summary>
/// Valida credenciais contra o usuário persistido. Retorna <c>null</c> tanto para usuário
/// inexistente quanto para senha incorreta — o chamador nunca deve distinguir os dois casos na
/// resposta, para não revelar quais nomes de usuário existem.
/// </summary>
public sealed class LoginHandler(IUserRepository users, IPasswordHasher passwordHasher)
    : ICommandHandler<LoginCommand, LoginResult?>
{
    public async Task<LoginResult?> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByUsernameAsync(command.Username, cancellationToken);
        if (user is null || !passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            return null;
        }

        return new LoginResult(user.Id, user.Username, user.DisplayName);
    }
}
