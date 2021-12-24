﻿using MongoDB.Bson;
using MongoDB.Bson.IO;
using System;
using System.Linq;

namespace EntityDb.MongoDb.Rewriters
{
    internal abstract class HoistedRewriterBase : BsonDocumentRewriter
    {
        protected readonly string[] _hoistedFieldNames;
        protected readonly string _parentFieldName;

        public HoistedRewriterBase(BsonWriter bsonWriter, string parentFieldName, string[] hoistedFieldNames) :
            base(bsonWriter)
        {
            _parentFieldName = parentFieldName;
            _hoistedFieldNames = hoistedFieldNames;
        }

        protected void RewriteHoisted(BsonElement[] bsonElements)
        {
            _bsonWriter.WriteStartDocument();

            foreach (var bsonElement in bsonElements)
            {
                if (_hoistedFieldNames.Contains(bsonElement.Name))
                {
                    _bsonWriter.WriteName(bsonElement.Name);
                }
                else
                {
                    _bsonWriter.WriteName(_parentFieldName + "." + bsonElement.Name);
                }

                Rewrite(bsonElement.Value);
            }

            _bsonWriter.WriteEndDocument();
        }
    }
}
