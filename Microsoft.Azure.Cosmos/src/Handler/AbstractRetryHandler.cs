//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Handlers
{
    using System;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Tracing;
    using Microsoft.Azure.Documents;

    internal abstract class AbstractRetryHandler : RequestHandler
    {
        internal abstract Task<IDocumentClientRetryPolicy> GetRetryPolicyAsync(RequestMessage request);

        public override async Task<ResponseMessage> SendAsync(
            RequestMessage request,
            CancellationToken cancellationToken)
        {
            using (ITrace childTrace = request.Trace.StartChild("Send Async", TraceComponent.RequestHandler, TraceLevel.Info))
            {
                request.Trace = childTrace;
                IDocumentClientRetryPolicy retryPolicyInstance = await this.GetRetryPolicyAsync(request);
                request.OnBeforeSendRequestActions += retryPolicyInstance.OnBeforeSendRequest;

                try
                {
                    return await RetryHandler.ExecuteHttpRequestAsync(
                        callbackMethod: (trace) =>
                        {
                            using (ITrace childTrace = trace.StartChild("Callback Method"))
                            {
                                request.Trace = childTrace;
                                return base.SendAsync(request, cancellationToken);
                            }
                        },
                        callShouldRetry: (cosmosResponseMessage, trace, token) =>
                        {
                            using (ITrace shouldRetryTrace = trace.StartChild("Call Should Retry"))
                            {
                                request.Trace = shouldRetryTrace;
                                return retryPolicyInstance.ShouldRetryAsync(cosmosResponseMessage, cancellationToken);
                            }
                        },
                        callShouldRetryException: (exception, trace, token) =>
                        {
                            using (ITrace shouldRetryTrace = trace.StartChild("Call Should Retry Exception"))
                            {
                                request.Trace = shouldRetryTrace;
                                return retryPolicyInstance.ShouldRetryAsync(exception, cancellationToken);
                            }
                        },
                        trace: request.Trace,
                        cancellationToken: cancellationToken);
                }
                catch (DocumentClientException ex)
                {
                    return ex.ToCosmosResponseMessage(request);
                }
                catch (CosmosException ex)
                {
                    return ex.ToCosmosResponseMessage(request);
                }
                catch (AggregateException ex)
                {
                    // TODO: because the SDK underneath this path uses ContinueWith or task.Result we need to catch AggregateExceptions here
                    // in order to ensure that underlying DocumentClientExceptions get propagated up correctly. Once all ContinueWith and .Result 
                    // is removed this catch can be safely removed.
                    AggregateException innerExceptions = ex.Flatten();
                    Exception docClientException = innerExceptions.InnerExceptions.FirstOrDefault(innerEx => innerEx is DocumentClientException);
                    if (docClientException != null)
                    {
                        return ((DocumentClientException)docClientException).ToCosmosResponseMessage(request);
                    }

                    throw;
                }
                finally
                {
                    request.OnBeforeSendRequestActions -= retryPolicyInstance.OnBeforeSendRequest;
                }
            } 
        }

        private static async Task<ResponseMessage> ExecuteHttpRequestAsync(
           Func<ITrace, Task<ResponseMessage>> callbackMethod,
           Func<ResponseMessage, ITrace, CancellationToken, Task<ShouldRetryResult>> callShouldRetry,
           Func<Exception, ITrace, CancellationToken, Task<ShouldRetryResult>> callShouldRetryException,
           ITrace trace,
           CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ShouldRetryResult result;

                try
                {
                    ResponseMessage cosmosResponseMessage = await callbackMethod(trace);
                    if (cosmosResponseMessage.IsSuccessStatusCode)
                    {
                        return cosmosResponseMessage;
                    }

                    result = await callShouldRetry(cosmosResponseMessage, trace, cancellationToken);

                    if (!result.ShouldRetry)
                    {
                        return cosmosResponseMessage;
                    }
                }
                catch (HttpRequestException httpRequestException)
                {
                    result = await callShouldRetryException(httpRequestException, trace, cancellationToken);
                    if (!result.ShouldRetry)
                    {
                        // Today we don't translate request exceptions into status codes since this was an error before
                        // making the request. TODO: Figure out how to pipe this as a response instead of throwing?
                        throw;
                    }
                }

                TimeSpan backoffTime = result.BackoffTime;
                if (backoffTime != TimeSpan.Zero)
                {
                    await Task.Delay(result.BackoffTime, cancellationToken);
                }
            }
        }
    }
}