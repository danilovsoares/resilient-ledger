using FluentValidation;

namespace Verity.Ledger.Application.Transactions.Commands.RegisterTransaction;

public sealed class RegisterTransactionValidator : AbstractValidator<RegisterTransactionCommand>
{
    public RegisterTransactionValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0m)
            .WithMessage("O valor do lançamento deve ser positivo.");

        RuleFor(x => x.IdempotencyKey)
            .NotEmpty()
            .WithMessage("O cabeçalho Idempotency-Key é obrigatório.")
            .MaximumLength(128);

        RuleFor(x => x.OccurredAt)
            .NotEqual(default(DateTimeOffset))
            .WithMessage("A data de ocorrência do lançamento é obrigatória.");

        RuleFor(x => x.Description)
            .MaximumLength(500);

        RuleFor(x => x.Type)
            .IsInEnum();
    }
}
