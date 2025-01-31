﻿namespace Ocelot.Request.Mapper
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;

    using Configuration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Http.Extensions;
    using Microsoft.Extensions.Primitives;

    using Responses;

    public class RequestMapper : IRequestMapper
    {
        private readonly string[] _unsupportedHeaders = { "host" };

        public async Task<Response<HttpRequestMessage>> Map(HttpRequest request, DownstreamRoute downstreamRoute)
        {
            try
            {
                var requestMessage = new HttpRequestMessage
                {
                    Content = await MapContent(request),
                    Method = MapMethod(request, downstreamRoute),
                    RequestUri = MapUri(request),
                    Version = downstreamRoute.DownstreamHttpVersion,
                };

                MapHeaders(request, requestMessage);

                return new OkResponse<HttpRequestMessage>(requestMessage);
            }
            catch (Exception ex)
            {
                return new ErrorResponse<HttpRequestMessage>(new UnmappableRequestError(ex));
            }
        }

        private static async Task<HttpContent> MapContent(HttpRequest request)
        {
            if (request.Body == null || (request.Body.CanSeek && request.Body.Length <= 0))
            {
                return null;
            }

            // Never change this to StreamContent again, I forgot it doesnt work in #464.
            var content = new ByteArrayContent(await ToByteArray(request.Body));

            if (!string.IsNullOrEmpty(request.ContentType))
            {
                content.Headers
                    .TryAddWithoutValidation("Content-Type", new[] { request.ContentType });
            }

            AddHeaderIfExistsOnRequest("Content-Language", content, request);
            AddHeaderIfExistsOnRequest("Content-Location", content, request);
            AddHeaderIfExistsOnRequest("Content-Range", content, request);
            AddHeaderIfExistsOnRequest("Content-MD5", content, request);
            AddHeaderIfExistsOnRequest("Content-Disposition", content, request);
            AddHeaderIfExistsOnRequest("Content-Encoding", content, request);

            return content;
        }

        private static void AddHeaderIfExistsOnRequest(string key, HttpContent content, HttpRequest request)
        {
            if (request.Headers.ContainsKey(key))
            {
                content.Headers
                    .TryAddWithoutValidation(key, request.Headers[key].ToArray());
            }
        }

        private static HttpMethod MapMethod(HttpRequest request, DownstreamRoute downstreamRoute)
        {
            if (!string.IsNullOrEmpty(downstreamRoute?.DownstreamHttpMethod))
            {
                return new HttpMethod(downstreamRoute.DownstreamHttpMethod);
            }

            return new HttpMethod(request.Method);
        }

        private static Uri MapUri(HttpRequest request) => new(request.GetEncodedUrl());

        private void MapHeaders(HttpRequest request, HttpRequestMessage requestMessage)
        {
            foreach (var header in request.Headers)
            {
                if (IsSupportedHeader(header))
                {
                    requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                }
            }
        }

        private bool IsSupportedHeader(KeyValuePair<string, StringValues> header)
        {
            return !_unsupportedHeaders.Contains(header.Key.ToLower());
        }

        private static async Task<byte[]> ToByteArray(Stream stream)
        {
            await using (stream)
            {
                using (var memStream = new MemoryStream())
                {
                    await stream.CopyToAsync(memStream);
                    return memStream.ToArray();
                }
            }
        }
    }
}
