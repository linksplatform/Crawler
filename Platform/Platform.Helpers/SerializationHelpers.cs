﻿using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace Platform.Helpers
{
    static public class SerializationHelpers
    {
        static readonly ConcurrentDictionary<Type, XmlSerializer> XmlSerializerCache = new ConcurrentDictionary<Type, XmlSerializer>();

        static XmlSerializer GetOrAddXmlSerializer<T>()
        {
            return XmlSerializerCache.GetOrAdd(typeof(T), type => new XmlSerializer(type));
        }

        static public T DeserializeFromXml<T>(string xmlString)
        {
            var serializer = GetOrAddXmlSerializer<T>();
            using (var reader = new StringReader(xmlString))
                return (T)serializer.Deserialize(reader);
        }

        static public void SerializeToFile<T>(string path, T obj)
        {
            var serializer = GetOrAddXmlSerializer<T>();
            using (var fileStream = File.Open(path, FileMode.Create))
            {
                serializer.Serialize(fileStream, obj);
                fileStream.Flush();
            }
        }

        static public string SerializeAsXmlString<T>(T obj)
        {
            var serializer = GetOrAddXmlSerializer<T>();
            var sb = new StringBuilder();
            using (var writer = new StringWriter(sb))
            {
                serializer.Serialize(writer, obj);
                writer.Flush();
                return sb.ToString();
            }
        }
    }
}
