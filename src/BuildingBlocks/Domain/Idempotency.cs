using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.BuildingBlocks.Domain;

/// <summary>Nom de l'en-tete HTTP d'idempotence.</summary>
public static class IdempotencyConstants
{
    /// <summary>En-tete a fournir sur les POST sensibles.</summary>
    public const string HeaderName = "Idempotency-Key";
}

/// <summary>
/// Trace d'une operation deja executee pour une cle d'idempotence donnee.
/// Permet de rejouer la meme reponse si le client reemet la requete
/// (double clic, rejeu reseau, retry automatique).
/// </summary>
public sealed class IdempotencyRecord
{
    /// <summary>Operation logique concernee (ex. <c>orders.create</c>).</summary>
    [MaxLength(80)]
    public string Operation { get; set; } = string.Empty;

    /// <summary>Cle fournie par le client.</summary>
    [MaxLength(128)]
    public string Key { get; set; } = string.Empty;

    /// <summary>Code HTTP renvoye la premiere fois.</summary>
    public int StatusCode { get; set; }

    /// <summary>Corps JSON renvoye la premiere fois.</summary>
    public string ResponseBody { get; set; } = "{}";

    /// <summary>Identifiant de la ressource creee ou modifiee.</summary>
    [MaxLength(64)]
    public string? ResourceId { get; set; }

    /// <summary>Date d'enregistrement.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Acces au journal d'idempotence.</summary>
public interface IIdempotencyStore
{
    /// <summary>Recherche une execution precedente.</summary>
    Task<IdempotencyRecord?> FindAsync(string operation, string key, CancellationToken cancellationToken);

    /// <summary>Enregistre le resultat d'une execution.</summary>
    Task SaveAsync(
        string operation,
        string key,
        int statusCode,
        string responseBody,
        string? resourceId,
        CancellationToken cancellationToken);
}

/// <summary>Implementation EF Core generique, utilisable par n'importe quel DbContext de service.</summary>
public sealed class EfIdempotencyStore<TContext>(TContext context) : IIdempotencyStore
    where TContext : DbContext
{
    public Task<IdempotencyRecord?> FindAsync(string operation, string key, CancellationToken cancellationToken) =>
        context.Set<IdempotencyRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Operation == operation && r.Key == key, cancellationToken);

    public async Task SaveAsync(
        string operation,
        string key,
        int statusCode,
        string responseBody,
        string? resourceId,
        CancellationToken cancellationToken)
    {
        context.Set<IdempotencyRecord>().Add(new IdempotencyRecord
        {
            Operation = operation,
            Key = key,
            StatusCode = statusCode,
            ResponseBody = responseBody,
            ResourceId = resourceId,
            CreatedAt = DateTimeOffset.UtcNow
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Course entre deux requetes portant la meme cle : la premiere a gagne, c'est suffisant.
            context.ChangeTracker.Clear();
        }
    }
}

/// <summary>Configuration EF de la table d'idempotence, a appeler depuis OnModelCreating.</summary>
public static class IdempotencyModelExtensions
{
    /// <summary>Declare la table <c>IdempotencyRecords</c> avec sa cle composite.</summary>
    public static ModelBuilder ApplyIdempotencyConfiguration(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("IdempotencyRecords");
            entity.HasKey(r => new { r.Operation, r.Key });
            entity.Property(r => r.ResponseBody).IsRequired();
            entity.HasIndex(r => r.CreatedAt);
        });

        return modelBuilder;
    }
}
