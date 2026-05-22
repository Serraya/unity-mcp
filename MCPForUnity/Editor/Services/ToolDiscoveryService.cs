using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    public class ToolDiscoveryService : IToolDiscoveryService
    {
        private static readonly HashSet<string> SupportedParameterTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "string",
            "boolean",
            "bool",
            "integer",
            "int",
            "long",
            "number",
            "float",
            "double",
            "object",
            "array",
            "list"
        };

        private Dictionary<string, ToolMetadata> _cachedTools;


        public List<ToolMetadata> DiscoverAllTools()
        {
            if (_cachedTools != null)
            {
                return _cachedTools.Values.ToList();
            }

            _cachedTools = new Dictionary<string, ToolMetadata>();

            // Primary scan via TypeCache (fast, but can miss project assemblies in some domain-reload states)
            var toolTypes = TypeCache.GetTypesWithAttribute<McpForUnityToolAttribute>();

            // Fallback scan via AppDomain (slower but exhaustive; mirrors CommandRegistry behaviour)
            var appDomainTypes = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch (Exception ex)
                    {
                        McpLog.Warn($"Failed to reflect types from assembly {a.FullName}: {ex.Message}");
                        return new Type[0];
                    }
                })
                .Where(t => t.GetCustomAttribute<McpForUnityToolAttribute>() != null);

            // Merge both scans, deduplicating by type
            var allToolTypes = toolTypes
                .Concat(appDomainTypes)
                .Distinct()
                .ToList();

            foreach (var type in allToolTypes)
            {
                McpForUnityToolAttribute toolAttr;
                try
                {
                    toolAttr = type.GetCustomAttribute<McpForUnityToolAttribute>();
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"Failed to read [McpForUnityTool] for {type.FullName}: {ex.Message}");
                    continue;
                }

                if (toolAttr == null)
                {
                    continue;
                }

                var metadata = ExtractToolMetadata(type, toolAttr);
                if (metadata != null)
                {
                    if (_cachedTools.ContainsKey(metadata.Name))
                    {
                        McpLog.Warn($"Duplicate tool name '{metadata.Name}' from {type.FullName}; overwriting previous registration.");
                    }
                    _cachedTools[metadata.Name] = metadata;
                    EnsurePreferenceInitialized(metadata);
                }
            }

            McpLog.Info($"Discovered {_cachedTools.Count} MCP tools via reflection", false);
            return _cachedTools.Values.ToList();
        }

        public ToolMetadata GetToolMetadata(string toolName)
        {
            if (_cachedTools == null)
            {
                DiscoverAllTools();
            }

            return _cachedTools.TryGetValue(toolName, out var metadata) ? metadata : null;
        }

        public List<ToolMetadata> GetEnabledTools()
        {
            return DiscoverAllTools()
                .Where(tool => IsToolEnabled(tool.Name))
                .ToList();
        }

        public bool IsToolEnabled(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
            {
                return false;
            }

            string key = GetToolPreferenceKey(toolName);
            if (EditorPrefs.HasKey(key))
            {
                return EditorPrefs.GetBool(key, true);
            }

            var metadata = GetToolMetadata(toolName);
            return metadata?.AutoRegister ?? false;
        }

        public void SetToolEnabled(string toolName, bool enabled)
        {
            if (string.IsNullOrEmpty(toolName))
            {
                return;
            }

            string key = GetToolPreferenceKey(toolName);
            EditorPrefs.SetBool(key, enabled);
        }

        private ToolMetadata ExtractToolMetadata(Type type, McpForUnityToolAttribute toolAttr)
        {
            try
            {
                // Get tool name
                string toolName = toolAttr.Name;
                if (string.IsNullOrEmpty(toolName))
                {
                    // Derive from class name: CaptureScreenshotTool -> capture_screenshot
                    toolName = ConvertToSnakeCase(type.Name.Replace("Tool", ""));
                }

                // Get description
                string description = toolAttr.Description ?? $"Tool: {toolName}";

                // Extract parameters
                var parameters = ExtractParameters(type);

                var metadata = new ToolMetadata
                {
                    Name = toolName,
                    Description = description,
                    StructuredOutput = toolAttr.StructuredOutput,
                    Parameters = parameters,
                    ClassName = type.Name,
                    Namespace = type.Namespace ?? "",
                    AssemblyName = type.Assembly.GetName().Name,
                    AutoRegister = toolAttr.AutoRegister,
                    RequiresPolling = toolAttr.RequiresPolling,
                    PollAction = string.IsNullOrEmpty(toolAttr.PollAction) ? "status" : toolAttr.PollAction,
                    MaxPollSeconds = toolAttr.MaxPollSeconds,
                    Group = toolAttr.Group ?? "core"
                };

                metadata.IsBuiltIn = StringCaseUtility.IsBuiltInMcpType(
                    type, metadata.AssemblyName, "MCPForUnity.Editor.Tools");

                return metadata;

            }
            catch (Exception ex)
            {
                McpLog.Error($"Failed to extract metadata for {type.Name}: {ex.Message}");
                return null;
            }
        }

        private List<ParameterMetadata> ExtractParameters(Type type)
        {
            var explicitSchema = ExtractExplicitParametersSchema(type);
            if (explicitSchema != null)
            {
                return ExtractParametersFromSchema(type, explicitSchema);
            }

            // Look for nested Parameters class
            var parametersType = type.GetNestedType("Parameters");
            if (parametersType == null)
            {
                return new List<ParameterMetadata>();
            }

            var parameters = new List<ParameterMetadata>();

            // Get all properties with [ToolParameter]
            var properties = parametersType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                var paramAttr = prop.GetCustomAttribute<ToolParameterAttribute>();
                if (paramAttr == null)
                    continue;

                string paramName = prop.Name;
                string paramType = GetParameterType(prop.PropertyType);

                parameters.Add(new ParameterMetadata
                {
                    Name = paramName,
                    Description = paramAttr.Description,
                    Type = paramType,
                    Required = paramAttr.Required,
                    DefaultValue = paramAttr.DefaultValue,
                    EnumValues = new List<string>(),
                    Aliases = new List<string>()
                });
            }

            return parameters;
        }

        private JToken ExtractExplicitParametersSchema(Type type)
        {
            var schemaProperty = type.GetProperty(
                "ParametersSchema",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

            if (schemaProperty == null)
            {
                return null;
            }

            if (!typeof(JToken).IsAssignableFrom(schemaProperty.PropertyType)
                && schemaProperty.PropertyType != typeof(string))
            {
                throw new InvalidOperationException(
                    $"{type.FullName}.ParametersSchema must return JArray, JObject, JToken, or string.");
            }

            object rawSchema = schemaProperty.GetValue(null);
            if (rawSchema == null)
            {
                throw new InvalidOperationException($"{type.FullName}.ParametersSchema returned null.");
            }

            if (rawSchema is JToken token)
            {
                return token;
            }

            if (rawSchema is string json)
            {
                if (string.IsNullOrWhiteSpace(json))
                {
                    throw new InvalidOperationException($"{type.FullName}.ParametersSchema returned an empty JSON string.");
                }

                try
                {
                    return JToken.Parse(json);
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException($"{type.FullName}.ParametersSchema contains invalid JSON: {ex.Message}");
                }
            }

            throw new InvalidOperationException(
                $"{type.FullName}.ParametersSchema returned unsupported value type {rawSchema.GetType().FullName}.");
        }

        private List<ParameterMetadata> ExtractParametersFromSchema(Type type, JToken schema)
        {
            if (schema is JArray parametersArray)
            {
                return ExtractParameterDefinitionArray(type, parametersArray);
            }

            if (schema is JObject schemaObject)
            {
                if (schemaObject["parameters"] is JArray nestedParameters)
                {
                    return ExtractParameterDefinitionArray(type, nestedParameters);
                }

                if (schemaObject["properties"] is JObject propertiesObject)
                {
                    return ExtractJsonSchemaProperties(type, schemaObject, propertiesObject);
                }
            }

            throw new InvalidOperationException(
                $"{type.FullName}.ParametersSchema must be a parameter array, an object with 'parameters', or a JSON schema object with 'properties'.");
        }

        private List<ParameterMetadata> ExtractParameterDefinitionArray(Type type, JArray parametersArray)
        {
            var parameters = new List<ParameterMetadata>();

            for (int i = 0; i < parametersArray.Count; i++)
            {
                if (parametersArray[i] is not JObject parameterObject)
                {
                    throw new InvalidOperationException(
                        $"{type.FullName}.ParametersSchema parameter at index {i} must be an object.");
                }

                string name = parameterObject.Value<string>("name");
                parameters.Add(BuildParameterMetadata(type, name, parameterObject, defaultRequired: true));
            }

            return parameters;
        }

        private List<ParameterMetadata> ExtractJsonSchemaProperties(Type type, JObject schemaObject, JObject propertiesObject)
        {
            var requiredNames = new HashSet<string>(StringComparer.Ordinal);
            if (schemaObject["required"] is JArray requiredArray)
            {
                foreach (var token in requiredArray)
                {
                    string requiredName = token.Value<string>();
                    if (!string.IsNullOrWhiteSpace(requiredName))
                    {
                        requiredNames.Add(requiredName);
                    }
                }
            }

            var parameters = new List<ParameterMetadata>();
            foreach (var property in propertiesObject.Properties())
            {
                if (property.Value is not JObject parameterObject)
                {
                    throw new InvalidOperationException(
                        $"{type.FullName}.ParametersSchema property '{property.Name}' must be an object.");
                }

                parameters.Add(BuildParameterMetadata(
                    type,
                    property.Name,
                    parameterObject,
                    defaultRequired: requiredNames.Contains(property.Name)));
            }

            return parameters;
        }

        private ParameterMetadata BuildParameterMetadata(
            Type type,
            string name,
            JObject parameterObject,
            bool defaultRequired)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException($"{type.FullName}.ParametersSchema contains a parameter without a name.");
            }

            string parameterType = ExtractParameterType(type, name, parameterObject, out bool nullableFromType);
            bool required = parameterObject.Value<bool?>("required") ?? defaultRequired;
            bool nullable = parameterObject.Value<bool?>("nullable") ?? nullableFromType;
            string itemsType = ExtractItemsType(parameterObject);
            if (!string.IsNullOrWhiteSpace(itemsType))
            {
                itemsType = NormalizeParameterType(type, $"{name}.items", itemsType);
            }

            return new ParameterMetadata
            {
                Name = name,
                Description = parameterObject.Value<string>("description"),
                Type = parameterType,
                Required = required,
                DefaultValue = ExtractDefaultValue(parameterObject),
                EnumValues = ExtractStringList(parameterObject["enum_values"] ?? parameterObject["enum"]),
                Aliases = ExtractStringList(parameterObject["aliases"]),
                Nullable = nullable,
                ItemsType = itemsType
            };
        }

        private string ExtractParameterType(
            Type type,
            string parameterName,
            JObject parameterObject,
            out bool nullable)
        {
            nullable = false;
            JToken typeToken = parameterObject["type"];
            string parameterType = "string";

            if (typeToken is JArray typeArray)
            {
                parameterType = null;
                foreach (var token in typeArray)
                {
                    string typeName = token.Value<string>();
                    if (string.Equals(typeName, "null", StringComparison.OrdinalIgnoreCase))
                    {
                        nullable = true;
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(typeName) && parameterType == null)
                    {
                        parameterType = typeName;
                    }
                }

                parameterType ??= "string";
            }
            else if (typeToken != null && typeToken.Type != JTokenType.Null)
            {
                parameterType = typeToken.Value<string>() ?? "string";
            }

            parameterType = NormalizeParameterType(type, parameterName, parameterType);
            return parameterType;
        }

        private string NormalizeParameterType(Type type, string parameterName, string parameterType)
        {
            if (string.IsNullOrWhiteSpace(parameterType))
            {
                return "string";
            }

            if (!SupportedParameterTypes.Contains(parameterType))
            {
                throw new InvalidOperationException(
                    $"{type.FullName}.ParametersSchema parameter '{parameterName}' has unsupported type '{parameterType}'.");
            }

            switch (parameterType.ToLowerInvariant())
            {
                case "bool":
                    return "boolean";
                case "int":
                case "long":
                    return "integer";
                case "float":
                case "double":
                    return "number";
                case "list":
                    return "array";
                default:
                    return parameterType.ToLowerInvariant();
            }
        }

        private static List<string> ExtractStringList(JToken token)
        {
            var values = new List<string>();
            if (token == null || token.Type == JTokenType.Null)
            {
                return values;
            }

            if (token is JArray array)
            {
                foreach (var item in array)
                {
                    string value = item.Value<string>();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        values.Add(value);
                    }
                }

                return values;
            }

            string singleValue = token.Value<string>();
            if (!string.IsNullOrWhiteSpace(singleValue))
            {
                values.Add(singleValue);
            }

            return values;
        }

        private static string ExtractDefaultValue(JObject parameterObject)
        {
            JToken token = parameterObject["default_value"] ?? parameterObject["default"];
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type == JTokenType.String)
            {
                return token.Value<string>();
            }

            return token.ToString(Formatting.None);
        }

        private static string ExtractItemsType(JObject parameterObject)
        {
            string itemsType = parameterObject.Value<string>("items_type");
            if (!string.IsNullOrWhiteSpace(itemsType))
            {
                return itemsType;
            }

            if (parameterObject["items"] is JObject itemsObject)
            {
                JToken typeToken = itemsObject["type"];
                if (typeToken != null && typeToken.Type != JTokenType.Null)
                {
                    return typeToken.Value<string>();
                }
            }

            return null;
        }

        private string GetParameterType(Type type)
        {
            // Handle nullable types
            if (Nullable.GetUnderlyingType(type) != null)
            {
                type = Nullable.GetUnderlyingType(type);
            }

            // Map C# types to JSON schema types
            if (type == typeof(string))
                return "string";
            if (type == typeof(int) || type == typeof(long))
                return "integer";
            if (type == typeof(float) || type == typeof(double))
                return "number";
            if (type == typeof(bool))
                return "boolean";
            if (type.IsArray || typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
                return "array";

            return "object";
        }

        private string ConvertToSnakeCase(string input) => StringCaseUtility.ToSnakeCase(input);

        public void InvalidateCache()
        {
            _cachedTools = null;
        }

        private void EnsurePreferenceInitialized(ToolMetadata metadata)
        {
            if (metadata == null || string.IsNullOrEmpty(metadata.Name))
            {
                return;
            }

            string key = GetToolPreferenceKey(metadata.Name);
            if (!EditorPrefs.HasKey(key))
            {
                bool defaultValue = metadata.AutoRegister || metadata.IsBuiltIn;
                EditorPrefs.SetBool(key, defaultValue);
            }
        }

        private static string GetToolPreferenceKey(string toolName)
        {
            return EditorPrefKeys.ToolEnabledPrefix + toolName;
        }

    }
}
