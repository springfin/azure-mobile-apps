using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JsonDiffPatch;
using Microsoft.Datasync.Client.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Datasync.Client.Offline.Queue;

public class UpdatePatchOperation : TableOperation
{
    private PatchDocument _patch;

    /// <summary>
    /// Creates a new <see cref="UpdateOperation"/> object.
    /// </summary>
    /// <param name="tableName">The name of the table that contains the affected item.</param>
    /// <param name="itemId">The ID of the affected item.</param>
    /// <param name="jObject"></param>
    internal UpdatePatchOperation(string tableName, string itemId)
        : base(TableOperationKind.UpdatePatch, tableName, itemId)
    {
    }

    public override bool SerializeItemToQueue => true;

    /// <summary>
    /// Collapse this operation with a new operation by cancellation of either operation.
    /// </summary>
    /// <param name="newOperation">The new operation.</param>
    public override void CollapseOperation(TableOperation newOperation)
    {
        if (newOperation.ItemId != ItemId)
        {
            throw new ArgumentException($"Cannot collapse update operation '{Id}' with '{newOperation.Id}' - Item IDs do not match", nameof(newOperation));
        }
        // An update followed by a delete is still a delete.  Cancel the update.
        if (newOperation is DeleteOperation)
        {
            Cancel();
            newOperation.Update();
        }
        if (newOperation is UpdatePatchOperation)
        {
            // don't cancel UpdatePatch
            // it'll be cancelled if the diff is empty
        }
    }

    /// <summary>
    /// Executes the operation on the offline store.
    /// </summary>
    /// <param name="store">The offline store.</param>
    /// <param name="item">The item to use for the store operation.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>A task that completes when the store operation is completed.</returns>
    public override async Task ExecuteOperationOnOfflineStoreAsync(IOfflineStore store, JObject item, CancellationToken cancellationToken = default)
    {
        if (_patch == null)
            return;

        item = await store.GetItemAsync(TableName, ItemId, cancellationToken).ConfigureAwait(false);

        var patcher = new JsonPatcher();
        var itemRoot = item.Root;

        patcher.Patch(ref itemRoot, _patch);

        item = JObject.FromObject(itemRoot);

        await store.UpsertAsync(TableName, new[]
        {
            item
        }, false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JObject> Initialize(IOfflineStore store, JObject item, ServiceSerializer serializer, CancellationToken cancellationToken)
    {
        var itemId = ServiceSerializer.GetId(item);
        var originalItem = await store.GetItemAsync(TableName, itemId, cancellationToken).ConfigureAwait(false);

        if (originalItem == null)
            throw new OfflineStoreException($"Item with ID '{itemId}' does not exist in the offline store.");

        originalItem = ServiceSerializer.RemoveSystemProperties(originalItem, out _);
        item = ServiceSerializer.RemoveSystemProperties(item, out _);

        originalItem = ReplaceDatesWithSerializedStrings(originalItem, serializer);

        var serverIdColumn = TableName + "Id";

        var serverIdPropertyOriginal = originalItem.Properties().FirstOrDefault(p => p.Name.Equals(serverIdColumn, StringComparison.OrdinalIgnoreCase));
        if (serverIdPropertyOriginal != null)
            originalItem.Remove(serverIdPropertyOriginal.Name);

        var serverIdProperty = item.Properties().FirstOrDefault(p => p.Name.Equals(serverIdColumn, StringComparison.OrdinalIgnoreCase));
        if (serverIdProperty != null)
            item.Remove(serverIdProperty.Name);

        var differ = new JsonDiffer();
        var patch = differ.Diff(originalItem, item, false);

        if (patch.Operations.Count == 0)
        {
            Cancel();
            return item;
        }

        var diffJson = $"{{\"patch\":{patch}}}";

        _patch = patch;

        Item = JObject.Parse(diffJson);
        Item["id"] = itemId;
        return item;
    }

    JObject ReplaceDatesWithSerializedStrings(JObject instance, ServiceSerializer serializer)
    {
        foreach (var property in instance.Properties())
        {
            if (property.Value.Type == JTokenType.Date)
            {
                instance[property.Name] = serializer.Serialize(property.Value);
            }
        }
        return instance;
    }

    /// <summary>
    /// Internal version of the <see cref="TableOperation.ExecuteOperationOnRemoteServiceAsync(DatasyncClient, CancellationToken)"/>, to execute
    /// the operation against a remote table.
    /// </summary>
    /// <param name="table">The remote table connection.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe.</param>
    /// <returns>A task that returns the operation result (or <c>null</c>) when complete.</returns>
    protected override Task<JToken> ExecuteRemoteOperationAsync(IRemoteTable table, CancellationToken cancellationToken)
        => table.UpdatePatchItemAsync(Item, cancellationToken);

    /// <summary>
    /// Validates that the operation can collapse with a new operation.
    /// </summary>
    /// <param name="newOperation">The new operation.</param>
    /// <exception cref="InvalidOperationException">when the operation cannot collapse with the new operation.</exception>
    public override void ValidateOperationCanCollapse(TableOperation newOperation)
    {
        if (newOperation.ItemId != ItemId)
        {
            throw new ArgumentException($"Cannot collapse update operation '{Id}' with '{newOperation.Id}' - Item IDs do not match", nameof(newOperation));
        }
        if (newOperation is InsertOperation)
        {
            throw new InvalidOperationException($"An update operation on item '{ItemId}' already exists in the operations queue.");
        }
    }
}