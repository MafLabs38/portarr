using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Accounts;

namespace Portarr.Api.V1.Accounts;

public static class AccountResultExtensions
{
    /// <summary>The problem answer of a refused account operation: its error picks the status, its message is safe to show.</summary>
    public static ObjectResult ToProblem<T>(this ControllerBase controller, AccountResult<T> result)
    {
        var (status, title) = result.Error switch
        {
            AccountError.Invalid => (StatusCodes.Status400BadRequest, "Invalid"),
            AccountError.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            AccountError.NotFound => (StatusCodes.Status404NotFound, "Not found"),
            _ => (StatusCodes.Status403Forbidden, "Not allowed"),
        };
        return controller.Problem(title: title, detail: result.Message, statusCode: status);
    }

    public static string? Client(this ControllerBase controller) => controller.HttpContext.Connection.RemoteIpAddress?.ToString();
}
