namespace Shop.Application.Common;

/// <summary>
/// Identifiant de l'instance applicative courante (PLT-1) : renvoyé dans l'en-tête X-Instance
/// et enregistré avec chaque run de synchronisation. Par défaut, le nom d'hôte (distinct pour chaque conteneur).
/// </summary>
public sealed record InstanceIdentity(string Id);
