using FluentValidation;

namespace Verity.Ledger.Application.Auth.Commands.Login;

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .MaximumLength(128); // Espelha users.username (ver UserConfiguration).

        RuleFor(x => x.Password)
            .NotEmpty()
            .MaximumLength(72); // BCrypt ignora silenciosamente bytes além de 72 — sem este
                                 // limite, senhas maiores pareceriam aceitas com mais entropia
                                 // do que realmente têm.
    }
}
