﻿namespace Dataverse.RestClient
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http.Json;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Manages requests that are sent in a single batch.
    /// </summary>
    public class BatchOperation: IBatchOperation
    {
        private readonly IDataverseClient dataverseClient;
        private readonly string baseAddress;

        private Dictionary<int, BatchRequest> requests = new();
        private int contentCount = -1;

        internal BatchOperation(IDataverseClient dataverseClient, string baseAddress) 
        {
            this.dataverseClient = dataverseClient;
            this.baseAddress = baseAddress;
        }

        internal int StoreRequest(HttpContent content, IChangeSet? changeSet = null)
        {
            contentCount++;
            requests.Add(contentCount, new BatchRequest(contentCount, content, changeSet));
            return contentCount;
        }

        /// <inheritdoc/>
        public int AddChangeSet(IChangeSet changeSet)
        {
            var content = changeSet.ToMultipartContent();
            return StoreRequest(content, changeSet);
        }

        /// <inheritdoc/>
        public int AddContent(HttpContent content)
        {
            return StoreRequest(content, null);
        }

        /// <summary>
        /// Send the batch request and return raw response from API. Use ProcessAsync to send request and parse the response..
        /// </summary>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <returns>Response from API.</returns>
        public async Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken = default)
        {
            var response = await this.dataverseClient.SendBatchAsync(Guid.NewGuid().ToString(), this.requests.Values.Select(t => t.HttpContent).ToList(), cancellationToken);
            return response;
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<BatchOperationResult>> ProcessAsync(CancellationToken cancellationToken = default)
        {
            var response = await this.SendAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await this.ParseResponseAsync(response.Content, cancellationToken);
            return result;
        }

        private async Task<List<BatchOperationResult>> ParseResponseAsync(HttpContent content, CancellationToken cancellationToken = default)
        {
            var multipartResponse = await content.ReadAsMultipartAsync(cancellationToken);
            List<BatchOperationResult> results = new();
            foreach (var (httpContent, index) in multipartResponse.Contents.Select((value, i) => (value, i)))
            {
                var request = this.requests[index];
                if (httpContent.IsMimeMultipartContent())
                {
                    var changeSetResults = await request.ChangeSet!.ProcessAsync(httpContent, cancellationToken);
                    results.Add(new BatchOperationResult(request.Index, request.HttpContent, changeSetResults));
                }
                else
                {
                    var mpResponse = await MultipartSingleResponse.Create(httpContent, cancellationToken);
                    results.Add(new BatchOperationResult(request.Index, request.HttpContent, mpResponse));
                }
            }
            return results;
        }

        /// <inheritdoc/>
        public HttpRequestMessage CreateHttpMessageRequest(string url, string method = "POST", string body = "{}") => new HttpRequestMessage(new HttpMethod(method), new Uri(this.baseAddress + url, UriKind.Absolute))
        {
            Version = new Version(1, 1),
            Content = JsonContent.Create(body)
        };


        /// <inheritdoc/>
        public IChangeSet CreateChangeSet()
        {
            return new ChangeSet(this.baseAddress);
        }
    }
}
