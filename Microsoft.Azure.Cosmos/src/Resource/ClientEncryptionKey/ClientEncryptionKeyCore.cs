﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos
{
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Documents;

    /// <summary>
    /// Provides operations for reading a specific data encryption key by Id.
    /// See <see cref="Cosmos.Database"/> for operations to create a data encryption key.
    /// </summary>
    internal class ClientEncryptionKeyCore : ClientEncryptionKey
    {
        /// <summary>
        /// Only used for unit testing
        /// </summary>
        protected ClientEncryptionKeyCore()
        {
        }

        public ClientEncryptionKeyCore(
            CosmosClientContext clientContext,
            DatabaseCore database,
            string keyId)
        {
            this.Id = keyId;
            this.ClientContext = clientContext;
            this.LinkUri = ClientEncryptionKeyCore.CreateLinkUri(clientContext, database, keyId);
            this.Database = database;
        }

        /// <inheritdoc/>
        public override string Id { get; }

        /// <summary>
        /// Returns a reference to a database object that contains this encryption key. 
        /// </summary>
        public Database Database { get; }

        public virtual string LinkUri { get; }

        public virtual CosmosClientContext ClientContext { get; }

        /// <inheritdoc/>
        public override async Task<ClientEncryptionKeyResponse> ReadAsync(
            RequestOptions requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            ClientEncryptionKeyResponse response = await this.ReadInternalAsync(requestOptions, diagnosticsContext: null, cancellationToken: cancellationToken);
            return response;
        }

        public static string CreateLinkUri(CosmosClientContext clientContext, DatabaseCore database, string keyId)
        {
            return clientContext.CreateLink(
                parentLink: database.LinkUri,
                uriPathSegment: Paths.ClientEncryptionKeysPathSegment,
                id: keyId);
        }

        private async Task<ClientEncryptionKeyResponse> ReadInternalAsync(
            RequestOptions requestOptions,
            CosmosDiagnosticsContext diagnosticsContext,
            CancellationToken cancellationToken)
        {
            ResponseMessage responseMessage = await this.ProcessStreamAsync(
                streamPayload: null,
                operationType: OperationType.Read,
                requestOptions: requestOptions,
                diagnosticsContext: diagnosticsContext,
                cancellationToken: cancellationToken);

            ClientEncryptionKeyResponse response = this.ClientContext.ResponseFactory.CreateClientEncryptionKeyResponse(this, responseMessage);
            Debug.Assert(response.Resource != null);
            return response;
        }

        private Task<ResponseMessage> ProcessStreamAsync(
            Stream streamPayload,
            OperationType operationType,
            RequestOptions requestOptions,
            CosmosDiagnosticsContext diagnosticsContext,
            CancellationToken cancellationToken = default)
        {
            return this.ClientContext.ProcessResourceOperationStreamAsync(
                resourceUri: this.LinkUri,
                resourceType: ResourceType.ClientEncryptionKey,
                operationType: operationType,
                cosmosContainerCore: null,
                partitionKey: null,
                streamPayload: streamPayload,
                requestOptions: requestOptions,
                requestEnricher: null,
                diagnosticsContext: diagnosticsContext,
                cancellationToken: cancellationToken);
        }
    }
}