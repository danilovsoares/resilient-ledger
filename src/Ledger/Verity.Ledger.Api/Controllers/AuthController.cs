using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Verity.Ledger.Api.Auth;
using Verity.Ledger.Api.RateLimiting;
using Verity.Ledger.Application.Auth.Commands.Login;
using Verity.Ledger.Application.Common;

namespace Verity.Ledger.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(
    ICommandHandler<LoginCommand, LoginResult?> loginHandler,
    IValidator<LoginCommand> validator,
    JwtOptions jwtOptions) : ControllerBase
{
    /// <summary>
    /// Autentica com usuário/senha e emite um JWT Bearer para uso nas duas Apis. Não distingue,
    /// na resposta, entre usuário inexistente e senha incorreta (ver <see cref="LoginHandler"/>).
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting(RateLimitingExtensions.LoginPolicyName)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var command = new LoginCommand(request.Username, request.Password);

        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            var modelState = new ModelStateDictionary();
            foreach (var error in validation.Errors)
            {
                modelState.AddModelError(error.PropertyName, error.ErrorMessage);
            }

            return ValidationProblem(modelState);
        }

        var result = await loginHandler.HandleAsync(command, cancellationToken);
        if (result is null)
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Credenciais inválidas",
                detail: "Usuário ou senha incorretos.");
        }

        var accessToken = JwtTokenFactory.Create(jwtOptions, subject: result.UserId.ToString(), name: result.Username);

        return Ok(new LoginResponse(accessToken, result.Username, result.DisplayName));
    }
}

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(string AccessToken, string Username, string DisplayName);
