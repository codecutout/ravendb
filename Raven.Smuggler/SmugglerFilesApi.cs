﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using NDesk.Options;
using Raven.Abstractions.Connection;
using Raven.Abstractions.Data;
using Raven.Abstractions.Exceptions;
using Raven.Abstractions.Smuggler;
using Raven.Abstractions.Smuggler.Data;
using Raven.Client.Document;
using Raven.Client.FileSystem;



namespace Raven.Smuggler
{
    public class SmugglerFilesApi : SmugglerFilesApiBase
    {
        private FilesStore primaryStore;
        private FilesStore secondaryStore;

        private DocumentStore documentStore;

        public SmugglerFilesApi(SmugglerFilesOptions options = null) : base(options ?? new SmugglerFilesOptions())
        {}

        public override async Task Between(SmugglerBetweenOptions<FilesConnectionStringOptions> betweenOptions)
        {
            if (betweenOptions.From == null)
                throw new ArgumentNullException("betweenOptions.From");

            if (betweenOptions.To == null)
                throw new ArgumentNullException("betweenOptions.To");            

            using (primaryStore = await CreateStore(betweenOptions.From))
            using (secondaryStore = await CreateStore(betweenOptions.To))
            using (documentStore = CreateDocumentStore(betweenOptions.To))
            {
                Operations = new SmugglerBetweenRemoteFilesOperations(() => primaryStore, () => secondaryStore, () => documentStore);

                await base.Between(betweenOptions);
            }              
        }

        public override async Task<ExportFilesResult> ExportData(SmugglerExportOptions<FilesConnectionStringOptions> exportOptions)
        {
            if (exportOptions.From == null)
                throw new ArgumentNullException("exportOptions");

            using (primaryStore = await CreateStore(exportOptions.From))
            using (documentStore = CreateDocumentStore(exportOptions.From))
			{
                Operations = new SmugglerRemoteFilesOperations(() => primaryStore, () => documentStore);

				return await base.ExportData(exportOptions);
			}
        }

        public override async Task ImportData(SmugglerImportOptions<FilesConnectionStringOptions> importOptions)
        {
            if (importOptions.To == null)
                throw new ArgumentNullException("importOptions");

            using (primaryStore = await CreateStore(importOptions.To))
            using (documentStore = CreateDocumentStore(importOptions.To))
            {
                Operations = new SmugglerRemoteFilesOperations(() => primaryStore, () => documentStore);

                await base.ImportData(importOptions);
            }
        }

        internal static async Task<FilesStore> CreateStore(FilesConnectionStringOptions options)
        {
            var credentials = options.Credentials as NetworkCredential;
            if (credentials == null)
            {
                credentials = CredentialCache.DefaultNetworkCredentials;
            }
            else if ((String.IsNullOrWhiteSpace(credentials.UserName) || String.IsNullOrWhiteSpace(credentials.Password)))
            {
                credentials = CredentialCache.DefaultNetworkCredentials;
            }

            var store = new FilesStore
            {
                Credentials = credentials,
                ApiKey = options.ApiKey,
                Url = options.Url,
                DefaultFileSystem = options.DefaultFileSystem,
            };

            store.Initialize();

            await ValidateThatServerIsUpAndFilesystemExists(options, store);

            return store;
        }

        internal static async Task ValidateThatServerIsUpAndFilesystemExists(FilesConnectionStringOptions server, FilesStore s)
        {
            var shouldDispose = false;

            try
            {
                var commands = !string.IsNullOrEmpty(server.DefaultFileSystem)
                                   ? s.AsyncFilesCommands.ForFileSystem(server.DefaultFileSystem)
                                   : s.AsyncFilesCommands;

                await commands.GetStatisticsAsync(); // check if file system exist
            }
            catch (Exception e)
            {
                shouldDispose = true;

                var responseException = e as ErrorResponseException;
                if (responseException != null && responseException.StatusCode == HttpStatusCode.ServiceUnavailable && responseException.Message.StartsWith("Could not find a file system named"))
                    throw new SmugglerException(
                        string.Format(
                            "Smuggler does not support file system creation (file system '{0}' on server '{1}' must exist before running Smuggler).",
                            server.DefaultFileSystem,
                            s.Url), e);


                if (e.InnerException != null)
                {
                    var webException = e.InnerException as WebException;
                    if (webException != null)
                    {
                        throw new SmugglerException(string.Format("Smuggler encountered a connection problem: '{0}'.", webException.Message), webException);
                    }
                } throw new SmugglerException(string.Format("Smuggler encountered a connection problem: '{0}'.", e.Message), e);
            }
            finally
            {
                if (shouldDispose)
                    s.Dispose();
            }
        }

        internal static DocumentStore CreateDocumentStore(FilesConnectionStringOptions options)
        {
            var credentials = options.Credentials as NetworkCredential;
            if (credentials == null)
            {
                credentials = CredentialCache.DefaultNetworkCredentials;
            }
            else if ((String.IsNullOrWhiteSpace(credentials.UserName) || String.IsNullOrWhiteSpace(credentials.Password)))
            {
                credentials = CredentialCache.DefaultNetworkCredentials;
            }

            var store = new DocumentStore
            {
                Credentials = credentials,
                ApiKey = options.ApiKey,
                Url = options.Url,
            };

            store.Initialize();

            return store;
        }
    }
}
