namespace Sitecore.Support.ContentSearch
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml;
    using Sitecore.ContentSearch;
    using Sitecore.ContentSearch.LuceneProvider;
    using Sitecore.ContentSearch.Utilities;
    using Sitecore.Diagnostics;
    using Sitecore.Exceptions;
    using Sitecore.Reflection;
    using Sitecore.Xml;

    using Debug = System.Diagnostics.Debug;

    public class FieldMap : IFieldMap
    {
        private readonly Dictionary<string, AbstractSearchFieldConfiguration> fieldNameMap = new Dictionary<string, AbstractSearchFieldConfiguration>();

        private readonly Dictionary<string, AbstractSearchFieldConfiguration> fieldTypeNameMap = new Dictionary<string, AbstractSearchFieldConfiguration>();

        public FieldMap()
        {
            this.AvailableTypes = new List<LuceneSearchFieldConfiguration>();
        }

        public List<LuceneSearchFieldConfiguration> AvailableTypes { get; set; }

        public void AddTypeMatch(XmlNode configNode)
        {
            Assert.ArgumentNotNull(configNode, "configNode");

            var settingType = XmlUtil.GetAttribute("settingType", configNode, null);
            var name = XmlUtil.GetAttribute("type", configNode, null);
            var typeName = Type.GetType(name).FullName;
            var xmlAttributes = XmlUtil.GetAttributes(configNode);

            if (settingType == null || xmlAttributes == null || typeName == null)
            {
                throw new ConfigurationException("Unable to process 'AddTypeMatch' config section.");
            }

            var settingTypeInfo = ReflectionUtil.GetTypeInfo(settingType);
            var attributeDictionary = xmlAttributes.Keys.Cast<string>().ToDictionary(attribute => attribute, attribute => xmlAttributes[attribute]);
            this.AddTypeMatch(typeName, settingTypeInfo, attributeDictionary, configNode);
        }

        public void AddTypeMatch(string typeName, Type settingType, IDictionary<string, string> attributes, XmlNode configNode)
        {
            Assert.ArgumentNotNullOrEmpty(typeName, "typeName");
            Assert.ArgumentNotNull(settingType, "settingType");

            var type = (LuceneSearchFieldConfiguration)ReflectionUtility.CreateInstance(settingType, null, null, typeName, attributes, configNode);
            Assert.IsNotNull(type, string.Format("Unable to create : {0}", settingType));
            this.AvailableTypes.Add(type);
        }

        public void AddFieldByFieldName([NotNull] XmlNode configNode)
        {
            Assert.ArgumentNotNull(configNode, "configNode");
            Assert.ArgumentNotNull(configNode.Attributes, "configNode.Attributes");

            var fieldName = XmlUtil.GetAttribute("fieldName", configNode, null);
            var xmlAttributes = XmlUtil.GetAttributes(configNode);
            var attributeDictionary = xmlAttributes.Keys.Cast<string>().ToDictionary(attribute => attribute, attribute => xmlAttributes[attribute]);
            var settingType = configNode.Attributes["settingType"].Value;

            this.AddFieldByFieldName(fieldName, ReflectionUtil.GetTypeInfo(settingType), attributeDictionary, configNode);
        }

        public void AddFieldByFieldName(string fieldName, Type settingType, IDictionary<string, string> attributes, XmlNode configNode)
        {
            Assert.ArgumentNotNull(fieldName, "fieldName");

            AbstractSearchFieldConfiguration fieldConfiguration;

            if (settingType == null)
            {
                fieldConfiguration = new AbstractSearchFieldConfiguration
                {
                    Attributes = attributes
                };
            }
            else
            {
                Assert.ArgumentNotNull(settingType, "settingType");
                Assert.ArgumentNotNull(attributes, "attributes");
                fieldConfiguration = (AbstractSearchFieldConfiguration)ReflectionUtility.CreateInstance(settingType, fieldName, null, null, attributes, configNode);
            }

            Assert.IsNotNull(fieldConfiguration, string.Format("Unable to create : {0}", settingType));

            this.fieldNameMap[fieldName.ToLowerInvariant()] = fieldConfiguration;
        }

        public void AddFieldByFieldTypeName([NotNull] XmlNode configNode)
        {
            Assert.ArgumentNotNull(configNode, "configNode");
            Assert.ArgumentNotNull(configNode.Attributes, "configNode");

            var xmlAttributes = XmlUtil.GetAttributes(configNode);
            var attributeDictionary = xmlAttributes.Keys.Cast<string>().ToDictionary(attribute => attribute, attribute => xmlAttributes[attribute]);

            var fieldTypeNames = configNode.Attributes["fieldTypeName"].Value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            var settingType = configNode.Attributes["settingType"].Value;

            this.AddFieldByFieldTypeName(ReflectionUtil.GetTypeInfo(settingType), fieldTypeNames, attributeDictionary, configNode);
        }

        public void AddFieldByFieldTypeName(Type settingType, IEnumerable<string> fieldTypeNames, IDictionary<string, string> attributes, XmlNode configNode)
        {
            Assert.ArgumentNotNull(fieldTypeNames, "fieldTypeNames");
            foreach (var fieldTypeName in fieldTypeNames)
            {
                Assert.ArgumentNotNull(settingType, "settingType");
                Assert.ArgumentNotNull(attributes, "attributes");

                var type = (AbstractSearchFieldConfiguration)ReflectionUtility.CreateInstance(settingType, null, null, fieldTypeName, attributes, configNode);
                Assert.IsNotNull(type, string.Format("Unable to create : {0} / {1}", settingType, fieldTypeName));
                this.Add(type);
            }
        }

        public void Add([NotNull] AbstractSearchFieldConfiguration abstractSearchField)
        {
            Assert.ArgumentNotNull(abstractSearchField, "AbstractSearchFieldConfiguration");
            Assert.ArgumentNotNull(abstractSearchField.FieldTypeName, "FieldTypeName");

            this.fieldTypeNameMap[abstractSearchField.FieldTypeName.ToLowerInvariant()] = abstractSearchField;
        }

        public AbstractSearchFieldConfiguration GetFieldConfiguration([NotNull] IIndexableDataField field)
        {
            return this.GetFieldConfiguration(field, f => true);
        }

        public AbstractSearchFieldConfiguration GetFieldConfiguration(IIndexableDataField field, Func<AbstractSearchFieldConfiguration, bool> fieldVisitorFunc)
        {
            Assert.ArgumentNotNull(field, "field");

            AbstractSearchFieldConfiguration abstractSearchField;

            if (!string.IsNullOrEmpty(field.Name) && this.fieldNameMap.TryGetValue(field.Name.ToLower(), out abstractSearchField) && fieldVisitorFunc(abstractSearchField))
            {
                return abstractSearchField;
            }

            if (this.fieldTypeNameMap.TryGetValue(field.TypeKey, out abstractSearchField) && fieldVisitorFunc(abstractSearchField))
            {
                return abstractSearchField;
            }

            Type fieldType = Type.GetType(field.TypeKey, false, true);

            if (fieldType != null)
            {
                abstractSearchField = this.GetFieldConfiguration(fieldType);

                if (abstractSearchField != null && fieldVisitorFunc(abstractSearchField))
                    return abstractSearchField;
            }

            if (field.FieldType != null)
            {
                abstractSearchField = this.GetFieldConfiguration(field.FieldType);

                if (abstractSearchField != null && fieldVisitorFunc(abstractSearchField))
                    return abstractSearchField;
            }

            return null;
        }

        public AbstractSearchFieldConfiguration GetFieldConfiguration([NotNull] string fieldName)
        {
            Assert.ArgumentNotNull(fieldName, "fieldName");

            AbstractSearchFieldConfiguration abstractSearchField;

            if (this.fieldNameMap.TryGetValue(fieldName.ToLower(), out abstractSearchField))
            {
                return abstractSearchField;
            }

            return null;
        }

        public AbstractSearchFieldConfiguration GetFieldConfiguration(Type returnType)
        {
            Assert.ArgumentNotNull(returnType, "returnType");

            var matchingTypes = this.AvailableTypes.Where(x => x.Type == returnType).ToArray();

            if (!matchingTypes.Any())
            {
                return null;
            }

            return matchingTypes.First();
        }

        public AbstractSearchFieldConfiguration GetFieldConfigurationByFieldTypeName([NotNull] string fieldTypeName)
        {
            Assert.ArgumentNotNull(fieldTypeName, "fieldTypeName");

            AbstractSearchFieldConfiguration abstractSearchField;

            if (this.fieldTypeNameMap.TryGetValue(fieldTypeName.ToLowerInvariant(), out abstractSearchField))
            {
                return abstractSearchField;
            }

            return null;
        }
    }
}