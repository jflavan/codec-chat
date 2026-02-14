using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Codec.Api.Services;

/// <summary>
/// Stores files in Azure Blob Storage.
/// Uses Managed Identity via <c>DefaultAzureCredential</c> — no connection strings needed.
/// Each logical container (e.g. "avatars", "images") maps to a blob container.
/// </summary>
public class AzureBlobStorageService : IFileStorageService
{
    private readonly BlobServiceClient _blobServiceClient;

    /// <param name="blobServiceClient">
    /// Pre-configured <see cref="BlobServiceClient"/> (injected via DI with <c>DefaultAzureCredential</c>).
    /// </param>
    public AzureBlobStorageService(BlobServiceClient blobServiceClient)
    {
        _blobServiceClient = blobServiceClient;
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(string containerName, string blobPath, Stream stream, string contentType, CancellationToken cancellationToken = default)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);

        var blobClient = containerClient.GetBlobClient(blobPath);
        await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: cancellationToken);

        return blobClient.Uri.ToString();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string containerName, string blobPath, CancellationToken cancellationToken = default)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.DeleteBlobIfExistsAsync(blobPath, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteByPrefixAsync(string containerName, string prefix, CancellationToken cancellationToken = default)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

        await foreach (var blob in containerClient.GetBlobsAsync(traits: BlobTraits.None, states: BlobStates.None, prefix: prefix, cancellationToken: cancellationToken))
        {
            await containerClient.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
        }
    }
}
