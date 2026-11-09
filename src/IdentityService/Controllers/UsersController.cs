using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.IdentityService.Dtos;
using GestionCommandes.IdentityService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GestionCommandes.IdentityService.Controllers;

/// <summary>Administration des comptes utilisateurs. Reserve au role Admin.</summary>
[ApiController]
[Route("api/users")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public sealed class UsersController(UserService users) : ControllerBase
{
    /// <summary>Liste paginee des comptes, avec filtres.</summary>
    /// <response code="200">Page de comptes.</response>
    [HttpGet]
    [ProducesResponseType<PagedResult<UserResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<UserResponse>>> Search(
        [FromQuery] UserQuery query,
        CancellationToken cancellationToken)
        => Ok(await users.SearchAsync(query, cancellationToken));

    /// <summary>Recupere un compte.</summary>
    /// <response code="200">Compte trouve.</response>
    /// <response code="404">Compte inexistant.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponse>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await users.GetAsync(id, cancellationToken));

    /// <summary>Cree un compte.</summary>
    /// <response code="201">Compte cree.</response>
    /// <response code="409">Identifiant deja utilise.</response>
    [HttpPost]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> Create(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var created = await users.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Met a jour le profil et les roles d'un compte.</summary>
    /// <response code="200">Compte mis a jour.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponse>> Update(
        Guid id,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
        => Ok(await users.UpdateAsync(id, request, cancellationToken));

    /// <summary>Active ou desactive un compte.</summary>
    /// <response code="200">Nouvel etat du compte.</response>
    /// <response code="409">Dernier administrateur actif : desactivation refusee.</response>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> SetStatus(
        Guid id,
        [FromBody] SetUserStatusRequest request,
        CancellationToken cancellationToken)
        => Ok(await users.SetStatusAsync(id, request.IsActive, cancellationToken));

    /// <summary>Reinitialise le mot de passe d'un compte.</summary>
    /// <response code="204">Mot de passe reinitialise.</response>
    [HttpPost("{id:guid}/reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword(
        Guid id,
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await users.ResetPasswordAsync(id, request, cancellationToken);

        return NoContent();
    }

    /// <summary>Supprime un compte.</summary>
    /// <response code="204">Compte supprime.</response>
    /// <response code="409">Dernier administrateur : suppression refusee.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await users.DeleteAsync(id, cancellationToken);

        return NoContent();
    }
}
