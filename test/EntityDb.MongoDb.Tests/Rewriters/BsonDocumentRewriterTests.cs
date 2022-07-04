﻿using System;
using System.Linq;
using System.Text.RegularExpressions;
using EntityDb.MongoDb.Rewriters;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using Shouldly;
using Xunit;

namespace EntityDb.MongoDb.Tests.Rewriters;

public class BsonDocumentRewriterTests
{
    private static BsonDocument CreateSubDocument()
    {
        return new BsonDocument
        {
            ["Double"] = BsonDouble.Create(0.0),
            ["String"] = BsonString.Create(""),
            ["Boolean"] = BsonBoolean.True,
            ["TimeStamp"] = BsonDateTime.Create(DateTime.UtcNow),
            ["Null"] = BsonNull.Value,
            ["RegularExpression"] = BsonRegularExpression.Create(new Regex("$abc^")),
            ["Int32"] = BsonInt32.Create(0),
            ["Timestamp"] =
                BsonTimestamp.Create(Convert.ToInt64((DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds)),
            ["Int64"] = BsonInt64.Create((long)0),
            ["Decimal128"] = BsonDecimal128.Create((decimal)0.0)
        };
    }

    [Fact]
    public void CanRewriteFullBsonDocument()
    {
        // ARRANGE

        var originalBsonDocument = new BsonDocument
        {
            ["Document"] = CreateSubDocument(),
            ["Array"] = new BsonArray { CreateSubDocument(), CreateSubDocument() }
        };

        var expectedBson = originalBsonDocument.ToBson();

        var copyBsonDocument = new BsonDocument();

        using var bsonWriter = new BsonDocumentWriter(copyBsonDocument);

        var bsonDocumentRewriter = new BsonDocumentRewriter(bsonWriter);

        // ACT

        bsonDocumentRewriter.Rewrite(originalBsonDocument);

        var actualBson = copyBsonDocument.ToBson();

        // ASSERT

        actualBson.SequenceEqual(expectedBson).ShouldBeTrue();
    }

    [Fact]
    public void CannotWriteBsonDocumentWithJavaScript()
    {
        // ARRANGE

        var originalBsonDocument = new BsonDocument
        {
            ["JavaScript"] = BsonJavaScript.Create("function() { return true; }"),
            ["JavaScriptWithScope"] = BsonJavaScriptWithScope.Create("function(a) { return true; }")
        };

        var copyBsonDocument = new BsonDocument();

        using var bsonWriter = new BsonDocumentWriter(copyBsonDocument);

        var bsonDocumentRewriter = new BsonDocumentRewriter(bsonWriter);

        // ASSERT

        Should.Throw<NotSupportedException>(() => { bsonDocumentRewriter.Rewrite(originalBsonDocument); });
    }
}