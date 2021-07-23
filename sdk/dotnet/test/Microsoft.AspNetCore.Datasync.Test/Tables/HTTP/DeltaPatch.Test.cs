﻿// Copyright (c) Microsoft Corporation. All Rights Reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Datasync.Common.Test;
using Datasync.Common.Test.Extensions;
using Datasync.Common.Test.Models;
using Datasync.Common.Test.TestData;
using Datasync.Webservice;
using Microsoft.AspNetCore.Datasync.Extensions;
using Xunit;

namespace Microsoft.AspNetCore.Datasync.Test.Tables.HTTP
{
    [ExcludeFromCodeCoverage(Justification = "Test suite")]
    public class DeltaPatch_Tests
    {
        private readonly DateTimeOffset startTime = DateTimeOffset.Now;

        [Theory, CombinatorialData]
        public async Task BasicPatchTests(
            [CombinatorialRange(0, Movies.Count)] int index,
            [CombinatorialValues("movies", "movies_pagesize")] string table)
        {
            // Arrange
            var server = Program.CreateTestServer();
            var repository = server.GetRepository<InMemoryMovie>();
            var id = Utils.GetMovieId(index);
            var expected = repository.GetEntity(id).Clone();
            expected.Title = "Test Movie Title";
            expected.Rating = "PG-13";

            var patchDoc = new Dictionary<string, object>()
            {
                { "title", "Test Movie Title" },
                { "rating", "PG-13" }
            };

            // Act
            var response = await server.SendRequest(HttpMethod.Patch, $"tables/{table}/{id}", patchDoc, "application/json", null).ConfigureAwait(false);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = response.DeserializeContent<ClientMovie>();
            var stored = repository.GetEntity(id);

            AssertEx.SystemPropertiesSet(stored, startTime);
            AssertEx.SystemPropertiesChanged(expected, stored);
            AssertEx.SystemPropertiesMatch(stored, result);
            Assert.Equal<IMovie>(expected, result);
            AssertEx.ResponseHasConditionalHeaders(stored, response);
        }

        [Theory, CombinatorialData]
        public async Task CannotPatchSystemProperties(
            [CombinatorialValues("movies", "movies_pagesize")] string table,
            [CombinatorialValues("Id", "UpdatedAt", "Version")] string propName)
        {
            // Arrange
            Dictionary<string, string> propValues = new()
            {
                { "Id", "test-id" },
                { "UpdatedAt", "2018-12-31T05:00:00.000Z" },
                { "Version", "dGVzdA==" }
            };

            var server = Program.CreateTestServer();
            var repository = server.GetRepository<InMemoryMovie>();
            var id = Utils.GetMovieId(100);
            var expected = repository.GetEntity(id).Clone();
            var patchDoc = new Dictionary<string, object>()
            {
                { propName, propValues[propName] }
            };

            // Act
            var response = await server.SendRequest(HttpMethod.Patch, $"tables/{table}/{id}", patchDoc, "application/json", null).ConfigureAwait(false);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var stored = repository.GetEntity(id);
            Assert.Equal<IMovie>(expected, stored);
            Assert.Equal<ITableData>(expected, stored);
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound, "tables/movies/missing-id")]
        [InlineData(HttpStatusCode.NotFound, "tables/movies_pagesize/missing-id")]
        public async Task PatchFailureTests(HttpStatusCode expectedStatusCode, string relativeUri)
        {
            // Arrange
            var server = Program.CreateTestServer();
            var patchDoc = new Dictionary<string, object>()
            {
                { "Title", "Test Movie Title" },
                { "Rating", "PG-13" }
            };

            // Act
            var response = await server.SendRequest(HttpMethod.Patch, relativeUri, patchDoc, "application/json", null).ConfigureAwait(false);

            // Assert
            Assert.Equal(expectedStatusCode, response.StatusCode);
        }

        [Fact]
        public async Task PatchFailedWithWrongContentType()
        {
            // Arrange
            var server = Program.CreateTestServer();
            var repository = server.GetRepository<InMemoryMovie>();
            var id = Utils.GetMovieId(100);
            var expected = repository.GetEntity(id).Clone();
            var patchDoc = new Dictionary<string, object>()
            {
                { "Title", "Test Movie Title" },
                { "Rating", "PG-13" }
            };

            // Act
            var response = await server.SendRequest(HttpMethod.Patch, $"tables/movies/{id}", patchDoc, "application/json+problem", null).ConfigureAwait(false);

            // Assert
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
            var stored = repository.GetEntity(id);
            Assert.Equal<IMovie>(expected, stored);
            Assert.Equal<ITableData>(expected, stored);
        }

        [Fact]
        public async Task PatchFailedWithMalformedJson()
        {
            // Arrange
            var server = Program.CreateTestServer();
            var repository = server.GetRepository<InMemoryMovie>();
            var id = Utils.GetMovieId(100);
            var expected = repository.GetEntity(id).Clone();
            const string patchDoc = "{ \"some-malformed-json\": null";

            // Act
            var response = await server.SendRequest(HttpMethod.Patch, $"tables/movies/{id}", patchDoc, "application/json", null).ConfigureAwait(false);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var stored = repository.GetEntity(id);
            Assert.Equal<IMovie>(expected, stored);
            Assert.Equal<ITableData>(expected, stored);
        }

        [Theory]
        [InlineData("duration", 50)]
        [InlineData("duration", 370)]
        [InlineData("rating", "M")]
        [InlineData("rating", "PG-13 but not always")]
        [InlineData("title", "a")]
        [InlineData("title", "Lorem ipsum dolor sit amet, consectetur adipiscing elit accumsan.")]
        [InlineData("year", 1900)]
        [InlineData("year", 2035)]
        [InlineData("Duration", 50)]
        [InlineData("Duration", 370)]
        [InlineData("Rating", "M")]
        [InlineData("Rating", "PG-13 but not always")]
        [InlineData("Title", "a")]
        [InlineData("Title", "Lorem ipsum dolor sit amet, consectetur adipiscing elit accumsan.")]
        [InlineData("Year", 1900)]
        [InlineData("Year", 2035)]
        public async Task PatchValidationFailureTests(string propName, object propValue)
        {
            // Arrange
            string id = Utils.GetMovieId(100);
            var server = Program.CreateTestServer();
            var patchDoc = new Dictionary<string, object>()
            {
                { propName, propValue }
            };

            // Act
            var response = await server.SendRequest(HttpMethod.Patch, $"tables/movies/{id}", patchDoc, "application/json", null).ConfigureAwait(false);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory, CombinatorialData]
        public async Task AuthenticatedPatchTests(
            [CombinatorialValues(0, 1, 2, 3, 7, 14, 25)] int index,
            [CombinatorialValues(null, "failed", "success")] string userId,
            [CombinatorialValues("movies_rated", "movies_legal")] string table)
        {
            // Arrange
            var server = Program.CreateTestServer();
            var repository = server.GetRepository<InMemoryMovie>();
            var id = Utils.GetMovieId(index);
            var original = repository.GetEntity(id).Clone();
            var expected = repository.GetEntity(id).Clone();
            expected.Title = "TEST MOVIE TITLE"; // Upper Cased because of the PreCommitHook
            expected.Rating = "PG-13";

            var patchDoc = new Dictionary<string, object>()
            {
                { "Title", "Test Movie Title" },
                { "Rating", "PG-13" }
            };

            Dictionary<string, string> headers = new();
            Utils.AddAuthHeaders(headers, userId);

            // Act
            var response = await server.SendRequest(HttpMethod.Patch, $"tables/{table}/{id}", patchDoc, "application/json", headers).ConfigureAwait(false);
            var stored = repository.GetEntity(id);

            // Assert
            if (userId != "success")
            {
                var statusCode = table.Contains("legal") ? HttpStatusCode.UnavailableForLegalReasons : HttpStatusCode.Unauthorized;
                Assert.Equal(statusCode, response.StatusCode);
                Assert.Equal<IMovie>(original, stored);
                Assert.Equal<ITableData>(original, stored);
            }
            else
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var result = response.DeserializeContent<ClientMovie>();
                AssertEx.SystemPropertiesSet(stored, startTime);
                AssertEx.SystemPropertiesChanged(expected, stored);
                AssertEx.SystemPropertiesMatch(stored, result);
                Assert.Equal<IMovie>(expected, result);
                AssertEx.ResponseHasConditionalHeaders(stored, response);
            }
        }

        [Theory]
        [InlineData("If-Match", null, HttpStatusCode.OK)]
        [InlineData("If-Match", "\"dGVzdA==\"", HttpStatusCode.PreconditionFailed)]
        [InlineData("If-None-Match", null, HttpStatusCode.PreconditionFailed)]
        [InlineData("If-None-Match", "\"dGVzdA==\"", HttpStatusCode.OK)]
        [InlineData("If-Modified-Since", "Fri, 01 Mar 2019 15:00:00 GMT", HttpStatusCode.OK)]
        [InlineData("If-Modified-Since", "Sun, 03 Mar 2019 15:00:00 GMT", HttpStatusCode.PreconditionFailed)]
        [InlineData("If-Unmodified-Since", "Sun, 03 Mar 2019 15:00:00 GMT", HttpStatusCode.OK)]
        [InlineData("If-Unmodified-Since", "Fri, 01 Mar 2019 15:00:00 GMT", HttpStatusCode.PreconditionFailed)]
        public async Task ConditionalPatchTests(string headerName, string headerValue, HttpStatusCode expectedStatusCode)
        {
            // Arrange
            var server = Program.CreateTestServer();
            var repository = server.GetRepository<InMemoryMovie>();
            string id = Utils.GetMovieId(100);
            var entity = repository.GetEntity(id);
            entity.UpdatedAt = DateTimeOffset.Parse("Sat, 02 Mar 2019 15:00:00 GMT");
            Dictionary<string, string> headers = new() { { headerName, headerValue ?? entity.GetETag() } };
            var patchDoc = new Dictionary<string, object>()
            {
                { "Title", "Test Movie Title" },
                { "Rating", "PG-13" }
            };
            var expected = repository.GetEntity(id).Clone();

            // Act
            var response = await server.SendRequest(HttpMethod.Patch, $"tables/movies/{id}", patchDoc, "application/json", headers).ConfigureAwait(false);

            // Assert
            Assert.Equal(expectedStatusCode, response.StatusCode);
            var actual = response.DeserializeContent<ClientMovie>();
            var stored = repository.GetEntity(id).Clone();

            switch (expectedStatusCode)
            {
                case HttpStatusCode.OK:
                    // Do the replacement in the expected
                    expected.Title = "Test Movie Title";
                    expected.Rating = "PG-13";

                    AssertEx.SystemPropertiesSet(stored, startTime);
                    AssertEx.SystemPropertiesChanged(expected, stored);
                    AssertEx.SystemPropertiesMatch(stored, actual);
                    Assert.Equal<IMovie>(expected, actual);
                    AssertEx.ResponseHasConditionalHeaders(stored, response);
                    break;
                case HttpStatusCode.PreconditionFailed:
                    Assert.Equal<IMovie>(expected, actual);
                    AssertEx.SystemPropertiesMatch(expected, actual);
                    AssertEx.ResponseHasConditionalHeaders(expected, response);
                    break;
            }
        }

        [Theory, CombinatorialData]
        public async Task SoftDeletePatch_PatchDeletedItem_ReturnsGone([CombinatorialValues("soft", "soft_logged")] string table)
        {
            // Arrange
            const int index = 25;
            var server = Program.CreateTestServer();
            var id = Utils.GetMovieId(index);

            var patchDoc = new Dictionary<string, object>()
            {
                { "Title", "Test Movie Title" },
                { "Rating", "PG-13" }
            };

            // Act
            var response = await server.SendRequest(HttpMethod.Patch, $"tables/{table}/{id}", patchDoc, "application/json", null).ConfigureAwait(false);

            // Assert
            Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        }

        [Theory, CombinatorialData]
        public async Task SoftDeletePatch_CanUndeleteDeletedItem([CombinatorialValues("soft", "soft_logged")] string table)
        {
            // Arrange
            const int index = 25;
            var server = Program.CreateTestServer();
            var repository = server.GetRepository<SoftMovie>();
            var id = Utils.GetMovieId(index);
            var expected = repository.GetEntity(id).Clone();
            expected.Deleted = false;

            var patchDoc = new Dictionary<string, object>()
            {
                { "Deleted", false }
            };

            // Act
            var response = await server.SendRequest(HttpMethod.Patch, $"tables/{table}/{id}", patchDoc, "application/json", null).ConfigureAwait(false);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = response.DeserializeContent<ClientMovie>();
            var stored = repository.GetEntity(id);

            AssertEx.SystemPropertiesSet(stored, startTime);
            AssertEx.SystemPropertiesChanged(expected, stored);
            AssertEx.SystemPropertiesMatch(stored, result);
            Assert.Equal<IMovie>(expected, result);
            AssertEx.ResponseHasConditionalHeaders(stored, response);
        }

        [Theory, CombinatorialData]
        public async Task SoftDeletePatch_PatchNotDeletedItem([CombinatorialValues("soft", "soft_logged")] string table)
        {
            // Arrange
            const int index = 24;
            var server = Program.CreateTestServer();
            var repository = server.GetRepository<SoftMovie>();
            var id = Utils.GetMovieId(index);
            var expected = repository.GetEntity(id).Clone();
            expected.Title = "Test Movie Title";
            expected.Rating = "PG-13";

            var patchDoc = new Dictionary<string, object>()
            {
                { "Title", "Test Movie Title" },
                { "Rating", "PG-13" }
            };

            // Act
            var response = await server.SendRequest(HttpMethod.Patch, $"tables/{table}/{id}", patchDoc, "application/json", null).ConfigureAwait(false);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = response.DeserializeContent<ClientMovie>();
            var stored = repository.GetEntity(id);

            AssertEx.SystemPropertiesSet(stored, startTime);
            AssertEx.SystemPropertiesChanged(expected, stored);
            AssertEx.SystemPropertiesMatch(stored, result);
            Assert.Equal<IMovie>(expected, result);
            AssertEx.ResponseHasConditionalHeaders(stored, response);
        }
    }
}
