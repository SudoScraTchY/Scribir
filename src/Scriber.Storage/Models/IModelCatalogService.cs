namespace Scriber.Storage.Models;

/// <summary>
/// Service interface for retrieving the versioned model catalog manifest.
/// Implements PH1-MODEL-001 / PH1-MODEL-002.
/// </summary>
public interface IModelCatalogService
{
    ModelManifest GetCatalog();
}
