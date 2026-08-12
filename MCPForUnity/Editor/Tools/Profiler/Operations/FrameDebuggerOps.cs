using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Runtime.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class FrameDebuggerOps
    {
        private const int DefaultPageSize = 50;
        private const int MaximumPageSize = 500;
        private const int DefaultShaderPropertyLimit = 256;
        private const int MaximumShaderPropertyLimit = 1024;
        private const int MaximumMissingFields = 128;
        private const int MaximumWarnings = 64;
        private const int MaximumGenericFields = 64;

        private static readonly Type UtilType;
        private static readonly PropertyInfo EventCountProp;
        private static readonly MethodInfo EnableMethod;
        private static readonly MethodInfo GetFrameEventsMethod;
        private static readonly MethodInfo GetEventDataMethod;
        private static readonly MethodInfo GetEventInfoNameMethod;
        private static readonly MethodInfo GetBatchBreakCauseStringsMethod;
        private static readonly Type EventDataType;
        private static readonly bool Available;

        private static readonly BindingFlags InstanceFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly Dictionary<Type, Dictionary<string, MemberInfo>> MemberCache =
            new Dictionary<Type, Dictionary<string, MemberInfo>>();

        static FrameDebuggerOps()
        {
            try
            {
                UtilType = Type.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility, UnityEditor");
                UtilType ??= Type.GetType("UnityEditorInternal.FrameDebuggerUtility, UnityEditor");

                if (UtilType == null)
                    return;

                EventCountProp = UtilType.GetProperty("count", BindingFlags.Public | BindingFlags.Static)
                              ?? UtilType.GetProperty("eventsCount", BindingFlags.Public | BindingFlags.Static);

                EnableMethod = UtilType.GetMethod("SetEnabled", BindingFlags.Public | BindingFlags.Static,
                                   null, new[] { typeof(bool), typeof(int) }, null)
                            ?? UtilType.GetMethod("SetEnabled", BindingFlags.Public | BindingFlags.Static);

                GetFrameEventsMethod = UtilType.GetMethod("GetFrameEvents", BindingFlags.Public | BindingFlags.Static);
                GetEventInfoNameMethod = UtilType.GetMethod("GetFrameEventInfoName", BindingFlags.Public | BindingFlags.Static);
                GetBatchBreakCauseStringsMethod = UtilType.GetMethod(
                    "GetBatchBreakCauseStrings",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    Type.EmptyTypes,
                    null);

                EventDataType = Type.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData, UnityEditor")
                             ?? Type.GetType("UnityEditorInternal.FrameDebuggerEventData, UnityEditor");

                if (EventDataType != null)
                {
                    GetEventDataMethod = UtilType.GetMethod("GetFrameEventData", BindingFlags.Public | BindingFlags.Static,
                                             null, new[] { typeof(int), EventDataType }, null);
                }

                GetEventDataMethod ??= UtilType.GetMethod("GetFrameEventData", BindingFlags.Public | BindingFlags.Static);

                Available = EventCountProp != null && EnableMethod != null;
            }
            catch
            {
                Available = false;
            }
        }

        internal static object Enable(JObject @params)
        {
            if (!Available)
                return FrameDebuggerUnavailable();

            EditorApplication.ExecuteMenuItem("Window/Analysis/Frame Debugger");

            if (EditorApplication.isPlaying && !EditorApplication.isPaused)
            {
                return new ErrorResponse(
                    "Game must be paused before enabling Frame Debugger. "
                    + "Call manage_editor action=pause first, then retry frame_debugger_enable.");
            }

            try
            {
                InvokeSetEnabled(true);
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to enable Frame Debugger: {ex.Message}");
            }

            int eventCount = GetEventCount();
            return new SuccessResponse("Frame Debugger enabled.", new
            {
                enabled = true,
                event_count = eventCount,
                unity_version = Application.unityVersion,
            });
        }

        internal static object Disable(JObject @params)
        {
            if (!Available)
                return FrameDebuggerUnavailable();

            try
            {
                InvokeSetEnabled(false);
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to disable Frame Debugger: {ex.Message}");
            }

            return new SuccessResponse("Frame Debugger disabled.", new
            {
                enabled = false,
                unity_version = Application.unityVersion,
            });
        }

        internal static object GetEvents(JObject @params)
        {
            if (!Available)
                return FrameDebuggerUnavailable();

            var p = new ToolParams(@params);
            int pageSize = Clamp(p.GetInt("page_size") ?? DefaultPageSize, 1, MaximumPageSize);
            int cursor = Math.Max(0, p.GetInt("cursor") ?? 0);
            var diagnostics = new ExtractionDiagnostics();

            int totalEvents = GetEventCount();
            if (totalEvents == 0)
                return NoEventsListResponse(pageSize, cursor, diagnostics);

            object[] frameEvents = GetFrameEvents(diagnostics);

            int end = Math.Min(cursor + pageSize, totalEvents);
            var events = new List<object>();
            for (int eventIndex = cursor; eventIndex < end; eventIndex++)
            {
                object descriptor = GetDescriptor(frameEvents, eventIndex);
                object eventData = null;
                if (descriptor == null && EventDataAvailable())
                    TryGetEventData(eventIndex, diagnostics, out eventData);

                events.Add(BuildCompactEventRow(eventIndex, descriptor, eventData, diagnostics));
            }

            var result = new Dictionary<string, object>
            {
                ["events"] = events,
                ["total_events"] = totalEvents,
                ["page_size"] = pageSize,
                ["cursor"] = cursor,
                ["unity_version"] = Application.unityVersion,
                ["missing_fields"] = diagnostics.MissingFields,
                ["warnings"] = diagnostics.Warnings,
            };
            if (end < totalEvents)
                result["next_cursor"] = end;

            return new SuccessResponse($"Frame Debugger events {cursor}-{end - 1} of {totalEvents}.", result);
        }

        internal static object GetEventDetails(JObject @params)
        {
            if (!Available)
                return FrameDebuggerUnavailable();

            var p = new ToolParams(@params);
            var eventIndexResult = GetRequiredEventIndex(p);
            if (!eventIndexResult.IsSuccess)
                return new ErrorResponse(eventIndexResult.ErrorMessage);

            int eventIndex = eventIndexResult.Value;
            object validationError = ValidateEventIndex(eventIndex);
            if (validationError != null)
                return validationError;

            var diagnostics = new ExtractionDiagnostics();
            object[] frameEvents = GetFrameEvents(diagnostics);
            object descriptor = GetDescriptor(frameEvents, eventIndex);

            object eventData;
            var dataError = GetEventDataOrError(eventIndex, diagnostics, out eventData);
            if (dataError != null)
                return dataError;

            bool includeShaderProperties = p.GetBool("include_shader_properties", false);
            int maxShaderProperties = Clamp(
                p.GetInt("max_shader_properties") ?? DefaultShaderPropertyLimit,
                1,
                MaximumShaderPropertyLimit);

            var data = BuildEventDetails(eventIndex, descriptor, eventData, includeShaderProperties, maxShaderProperties, diagnostics);
            return new SuccessResponse($"Frame Debugger event {eventIndex} details.", data);
        }

        internal static object CaptureEventOutput(JObject @params)
        {
            if (!Available)
                return FrameDebuggerUnavailable();

            var p = new ToolParams(@params);
            var eventIndexResult = GetRequiredEventIndex(p);
            if (!eventIndexResult.IsSuccess)
                return new ErrorResponse(eventIndexResult.ErrorMessage);

            int eventIndex = eventIndexResult.Value;
            object validationError = ValidateEventIndex(eventIndex);
            if (validationError != null)
                return validationError;

            var diagnostics = new ExtractionDiagnostics();
            object eventData;
            var dataError = GetEventDataOrError(eventIndex, diagnostics, out eventData);
            if (dataError != null)
                return dataError;

            object renderTextureObject;
            if (!TryGetFirstValue(eventData, diagnostics, "event_data.render_target_render_texture", true,
                    out renderTextureObject, "m_RenderTargetRenderTexture", "m_RenderTargetRenderTextureCopy", "renderTargetRenderTexture", "rtTexture"))
            {
                return new ErrorResponse("FRAME_DEBUGGER_OUTPUT_UNAVAILABLE", new
                {
                    message = "Selected Frame Debugger event does not expose a render-target RenderTexture.",
                    event_index = eventIndex,
                    unity_version = Application.unityVersion,
                    missing_fields = diagnostics.MissingFields,
                    warnings = diagnostics.Warnings,
                });
            }

            var renderTexture = renderTextureObject as RenderTexture;
            if (renderTexture == null)
            {
                return new ErrorResponse("FRAME_DEBUGGER_OUTPUT_UNAVAILABLE", new
                {
                    message = "Selected Frame Debugger event output is not a RenderTexture.",
                    event_index = eventIndex,
                    actual_type = renderTextureObject.GetType().FullName,
                    unity_version = Application.unityVersion,
                    missing_fields = diagnostics.MissingFields,
                    warnings = diagnostics.Warnings,
                });
            }

            if (renderTexture.width <= 0 || renderTexture.height <= 0)
            {
                return new ErrorResponse("FRAME_DEBUGGER_OUTPUT_UNAVAILABLE", new
                {
                    message = "Selected Frame Debugger event output RenderTexture has invalid dimensions.",
                    event_index = eventIndex,
                    width = renderTexture.width,
                    height = renderTexture.height,
                    unity_version = Application.unityVersion,
                });
            }

            var outputPathResult = ResolveOutputPath(p.Get("output_path"), eventIndex);
            if (!outputPathResult.IsSuccess)
                return new ErrorResponse(outputPathResult.ErrorMessage);

            bool includeBase64 = p.GetBool("include_base64", false);
            Texture2D texture = null;
            RenderTexture previousActive = RenderTexture.active;
            string fullPath = outputPathResult.Value;
            byte[] pngBytes;
            bool sampledNonZeroPixelFound;

            try
            {
                RenderTexture.active = renderTexture;
                texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0, false);
                texture.Apply(false, false);
                sampledNonZeroPixelFound = SampleNonZeroPixels(texture);
                pngBytes = texture.EncodeToPNG();
            }
            catch (Exception ex)
            {
                return new ErrorResponse("FRAME_DEBUGGER_OUTPUT_CAPTURE_FAILED", new
                {
                    message = ex.Message,
                    event_index = eventIndex,
                    unity_version = Application.unityVersion,
                });
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            }

            long sizeBytes;
            object writeError;
            if (!TryWritePng(fullPath, pngBytes, eventIndex, diagnostics, out sizeBytes, out writeError))
                return writeError;

            var outputData = new Dictionary<string, object>
            {
                ["event_index"] = eventIndex,
                ["output_path"] = fullPath,
                ["width"] = renderTexture.width,
                ["height"] = renderTexture.height,
                ["size_bytes"] = sizeBytes,
                ["source"] = SerializeUnityObject(renderTexture),
                ["source_format"] = renderTexture.format.ToString(),
                ["source_dimension"] = renderTexture.dimension.ToString(),
                ["include_base64"] = includeBase64,
                ["image_base64_omitted"] = !includeBase64,
                ["sampled_non_zero_pixel_found"] = sampledNonZeroPixelFound,
                ["orientation"] = new Dictionary<string, object>
                {
                    ["vertical_flip_applied"] = false,
                    ["capture_method"] = "RenderTexture.active + Texture2D.ReadPixels + Texture2D.EncodeToPNG",
                    ["runtime_verification"] = "Inspect the PNG against the Frame Debugger Output preview to confirm semantic orientation.",
                },
                ["unity_version"] = Application.unityVersion,
                ["missing_fields"] = diagnostics.MissingFields,
                ["warnings"] = diagnostics.Warnings,
            };
            if (includeBase64)
                outputData["image_base64"] = Convert.ToBase64String(pngBytes);

            return new SuccessResponse($"Captured Frame Debugger event {eventIndex} output.", outputData);
        }

        private static object BuildEventDetails(
            int eventIndex,
            object descriptor,
            object eventData,
            bool includeShaderProperties,
            int maxShaderProperties,
            ExtractionDiagnostics diagnostics)
        {
            var sections = new Dictionary<string, object>();
            var availableSections = new List<string>();

            var identity = BuildIdentity(eventIndex, descriptor, eventData, diagnostics);
            AddSection(sections, availableSections, "identity", identity);

            var draw = BuildDraw(eventData, diagnostics);
            AddSection(sections, availableSections, "draw", draw);

            object shaderInfo = null;
            TryGetFirstValue(eventData, diagnostics, "event_data.shader_info", false,
                out shaderInfo, "m_ShaderInfo", "shaderInfo");

            var shader = BuildShader(eventData, shaderInfo, diagnostics);
            AddSection(sections, availableSections, "shader", shader);

            var renderTarget = BuildRenderTarget(eventData, diagnostics);
            AddSection(sections, availableSections, "render_target", renderTarget);

            var pipelineState = BuildPipelineState(eventData, diagnostics);
            AddSection(sections, availableSections, "pipeline_state", pipelineState);

            var batching = BuildBatching(eventData, diagnostics);
            AddSection(sections, availableSections, "batching", batching);

            var textures = BuildShaderResourceList(shaderInfo, diagnostics, maxShaderProperties, "textures",
                "m_Textures", "textures", "Textures", "textureInfos", "m_TextureInfos");
            if (textures.Count > 0)
            {
                sections["textures"] = textures;
                availableSections.Add("textures");
            }

            if (includeShaderProperties)
            {
                var shaderProperties = BuildShaderProperties(shaderInfo, diagnostics, maxShaderProperties);
                AddSection(sections, availableSections, "shader_properties", shaderProperties);
            }

            sections["unity_version"] = Application.unityVersion;
            sections["available_sections"] = availableSections;
            sections["missing_fields"] = diagnostics.MissingFields;
            sections["warnings"] = diagnostics.Warnings;
            sections["truncated"] = diagnostics.Truncated;
            sections["bounds"] = new Dictionary<string, object>
            {
                ["max_shader_properties"] = maxShaderProperties,
                ["maximum_shader_properties"] = MaximumShaderPropertyLimit,
            };

            return sections;
        }

        private static Dictionary<string, object> BuildCompactEventRow(
            int eventIndex,
            object descriptor,
            object eventData,
            ExtractionDiagnostics diagnostics)
        {
            var row = new Dictionary<string, object>
            {
                ["event_index"] = eventIndex,
                ["index"] = eventIndex,
            };

            string eventName = GetEventName(eventIndex);
            if (!string.IsNullOrEmpty(eventName))
                row["name"] = eventName;

            object eventType;
            if (descriptor != null &&
                TryGetFirstValue(descriptor, diagnostics, "frame_event.event_type", true,
                    out eventType, "m_Type", "type", "eventType"))
            {
                row["event_type"] = SerializeSimpleValue(eventType);
            }

            object objectRef;
            if (descriptor != null &&
                TryGetFirstValue(descriptor, diagnostics, "frame_event.object", true,
                    out objectRef, "m_Obj", "gameObjectInstanceID", "object", "objectInstanceID"))
            {
                row["object"] = SerializeObjectReference(objectRef);
            }
            else if (eventData != null &&
                     TryGetFirstValue(eventData, diagnostics, "event_data.object", false,
                         out objectRef, "m_Obj", "gameObjectInstanceID", "object", "objectInstanceID"))
            {
                row["object"] = SerializeObjectReference(objectRef);
            }

            return row;
        }

        private static Dictionary<string, object> BuildIdentity(
            int eventIndex,
            object descriptor,
            object eventData,
            ExtractionDiagnostics diagnostics)
        {
            var identity = new Dictionary<string, object>
            {
                ["event_index"] = eventIndex,
                ["index"] = eventIndex,
            };

            string eventName = GetEventName(eventIndex);
            if (!string.IsNullOrEmpty(eventName))
                identity["display_name"] = eventName;

            object eventType;
            if (descriptor != null &&
                TryGetFirstValue(descriptor, diagnostics, "frame_event.event_type", true,
                    out eventType, "m_Type", "type", "eventType"))
            {
                identity["event_type"] = SerializeSimpleValue(eventType);
            }

            object objectRef;
            if (descriptor != null &&
                TryGetFirstValue(descriptor, diagnostics, "frame_event.object", true,
                    out objectRef, "m_Obj", "gameObjectInstanceID", "object", "objectInstanceID"))
            {
                identity["object"] = SerializeObjectReference(objectRef);
            }
            else if (eventData != null &&
                     TryGetFirstValue(eventData, diagnostics, "event_data.object", false,
                         out objectRef, "m_Obj", "gameObjectInstanceID", "object", "objectInstanceID"))
            {
                identity["object"] = SerializeObjectReference(objectRef);
            }

            return identity;
        }

        private static Dictionary<string, object> BuildDraw(object eventData, ExtractionDiagnostics diagnostics)
        {
            var draw = new Dictionary<string, object>();
            AddFirstValue(draw, "draw_call_count", eventData, diagnostics, "event_data.draw_call_count", true,
                "m_DrawCallCount", "drawCallCount");
            AddFirstValue(draw, "vertices", eventData, diagnostics, "event_data.vertex_count", true,
                "m_VertexCount", "vertexCount");
            AddFirstValue(draw, "indices", eventData, diagnostics, "event_data.index_count", true,
                "m_IndexCount", "indexCount");
            AddFirstValue(draw, "instances", eventData, diagnostics, "event_data.instance_count", true,
                "m_InstanceCount", "instanceCount");

            object mesh;
            if (TryGetFirstValue(eventData, diagnostics, "event_data.mesh", false,
                    out mesh, "m_Mesh", "mesh"))
            {
                draw["mesh"] = SerializeObjectReference(mesh);
            }

            AddFirstValue(draw, "mesh_subset", eventData, diagnostics, "event_data.mesh_subset", false,
                "m_MeshSubset", "m_Subset", "subset");
            return draw;
        }

        private static Dictionary<string, object> BuildShader(object eventData, object shaderInfo, ExtractionDiagnostics diagnostics)
        {
            var shader = new Dictionary<string, object>();
            AddFirstValue(shader, "original_shader", eventData, diagnostics, "event_data.original_shader", true,
                "m_OriginalShaderName", "originalShaderName");
            AddFirstValue(shader, "used_shader", eventData, diagnostics, "event_data.used_shader", true,
                "m_RealShaderName", "m_ShaderName", "shaderName", "realShaderName");
            AddFirstValue(shader, "pass", eventData, diagnostics, "event_data.pass_name", true,
                "m_PassName", "passName");
            AddFirstValue(shader, "light_mode", eventData, diagnostics, "event_data.pass_light_mode", false,
                "m_PassLightMode", "passLightMode");
            AddFirstValue(shader, "subshader_index", eventData, diagnostics, "event_data.subshader_index", false,
                "m_SubShaderIndex", "m_SubshaderIndex", "subshaderIndex");
            AddFirstValue(shader, "shader_pass_index", eventData, diagnostics, "event_data.shader_pass_index", false,
                "m_ShaderPassIndex", "m_PassIndex", "passIndex");

            object directKeywords;
            if (TryGetFirstValue(eventData, diagnostics, "event_data.shader_keywords", false,
                    out directKeywords, "shaderKeywords", "m_ShaderKeywords", "shader_keywords"))
            {
                shader["shader_keywords"] = SerializeKeywordValue(directKeywords, diagnostics);
            }

            if (shaderInfo != null)
            {
                object keywords;
                if (TryGetFirstValue(shaderInfo, diagnostics, "shader_info.keywords", false,
                        out keywords, "m_Keywords", "keywords", "Keywords"))
                {
                    bool truncated;
                    shader["keyword_rows"] = SerializeResourceCollection(keywords, diagnostics, DefaultShaderPropertyLimit, out truncated);
                    diagnostics.Truncated |= truncated;
                }
            }

            return shader;
        }

        private static Dictionary<string, object> BuildRenderTarget(object eventData, ExtractionDiagnostics diagnostics)
        {
            var renderTarget = new Dictionary<string, object>();
            AddFirstValue(renderTarget, "name", eventData, diagnostics, "event_data.render_target_name", true,
                "m_RenderTargetName", "rtName", "renderTargetName");
            AddFirstValue(renderTarget, "width", eventData, diagnostics, "event_data.render_target_width", true,
                "m_RenderTargetWidth", "rtWidth", "renderTargetWidth");
            AddFirstValue(renderTarget, "height", eventData, diagnostics, "event_data.render_target_height", true,
                "m_RenderTargetHeight", "rtHeight", "renderTargetHeight");
            AddFirstValue(renderTarget, "format", eventData, diagnostics, "event_data.render_target_format", true,
                "m_RenderTargetFormat", "rtFormat", "renderTargetFormat");
            AddFirstValue(renderTarget, "dimension", eventData, diagnostics, "event_data.render_target_dimension", false,
                "m_RenderTargetDimension", "rtDimension", "renderTargetDimension");
            AddFirstValue(renderTarget, "load_action", eventData, diagnostics, "event_data.render_target_load_action", false,
                "m_RenderTargetLoadAction", "loadAction");
            AddFirstValue(renderTarget, "store_action", eventData, diagnostics, "event_data.render_target_store_action", false,
                "m_RenderTargetStoreAction", "storeAction");
            AddFirstValue(renderTarget, "depth_load_action", eventData, diagnostics, "event_data.render_target_depth_load_action", false,
                "m_RenderTargetDepthLoadAction", "depthLoadAction");
            AddFirstValue(renderTarget, "depth_store_action", eventData, diagnostics, "event_data.render_target_depth_store_action", false,
                "m_RenderTargetDepthStoreAction", "depthStoreAction");
            AddClearColor(renderTarget, eventData, diagnostics);
            AddFirstValue(renderTarget, "clear_depth", eventData, diagnostics, "event_data.clear_depth", false,
                "m_RenderTargetClearDepth", "m_ClearDepth", "clearDepth");
            AddFirstValue(renderTarget, "clear_stencil", eventData, diagnostics, "event_data.clear_stencil", false,
                "m_RenderTargetClearStencil", "m_ClearStencil", "clearStencil");
            AddFirstValue(renderTarget, "target_count", eventData, diagnostics, "event_data.render_target_count", false,
                "m_RenderTargetCount", "renderTargetCount");
            AddFirstValue(renderTarget, "has_depth_texture", eventData, diagnostics, "event_data.render_target_has_depth_texture", false,
                "m_RenderTargetHasDepthTexture", "hasDepthTexture");
            AddFirstValue(renderTarget, "has_stencil_bits", eventData, diagnostics, "event_data.render_target_has_stencil_bits", false,
                "m_RenderTargetHasStencilBits", "hasStencilBits");
            AddFirstValue(renderTarget, "memoryless", eventData, diagnostics, "event_data.memoryless", false,
                "m_RenderTargetMemoryless", "memoryless");
            AddFirstValue(renderTarget, "is_back_buffer", eventData, diagnostics, "event_data.is_back_buffer", false,
                "m_RenderTargetIsBackBuffer", "isBackBuffer");
            AddFirstValue(renderTarget, "foveated_rendering", eventData, diagnostics, "event_data.foveated_rendering", false,
                "m_RenderTargetFoveatedRenderingMode", "foveatedRendering");
            AddFirstValue(renderTarget, "cubemap_face", eventData, diagnostics, "event_data.cubemap_face", false,
                "m_RenderTargetCubemapFace", "cubemapFace");

            object renderTexture;
            if (TryGetFirstValue(eventData, diagnostics, "event_data.render_target_render_texture", false,
                    out renderTexture, "m_RenderTargetRenderTexture", "m_RenderTargetRenderTextureCopy", "renderTargetRenderTexture", "rtTexture"))
            {
                renderTarget["render_texture"] = SerializeObjectReference(renderTexture);
            }

            return renderTarget;
        }

        private static void AddClearColor(
            Dictionary<string, object> renderTarget,
            object eventData,
            ExtractionDiagnostics diagnostics)
        {
            object r;
            object g;
            object b;
            object a;
            bool hasR = TryGetFirstValue(eventData, diagnostics, "event_data.render_target_clear_color_r", false,
                out r, "m_RenderTargetClearColorR", "m_ClearColorR", "clearColorR");
            bool hasG = TryGetFirstValue(eventData, diagnostics, "event_data.render_target_clear_color_g", false,
                out g, "m_RenderTargetClearColorG", "m_ClearColorG", "clearColorG");
            bool hasB = TryGetFirstValue(eventData, diagnostics, "event_data.render_target_clear_color_b", false,
                out b, "m_RenderTargetClearColorB", "m_ClearColorB", "clearColorB");
            bool hasA = TryGetFirstValue(eventData, diagnostics, "event_data.render_target_clear_color_a", false,
                out a, "m_RenderTargetClearColorA", "m_ClearColorA", "clearColorA");

            if (!hasR && !hasG && !hasB && !hasA)
                return;

            var clearColor = new Dictionary<string, object>();
            if (hasR)
                clearColor["r"] = SerializeSimpleValue(r);
            if (hasG)
                clearColor["g"] = SerializeSimpleValue(g);
            if (hasB)
                clearColor["b"] = SerializeSimpleValue(b);
            if (hasA)
                clearColor["a"] = SerializeSimpleValue(a);

            renderTarget["clear_color"] = clearColor;
        }

        private static Dictionary<string, object> BuildPipelineState(object eventData, ExtractionDiagnostics diagnostics)
        {
            var state = new Dictionary<string, object>();
            AddStateObject(state, "blend", eventData, diagnostics, "event_data.blend_state",
                "m_BlendState", "blendState");
            AddStateObject(state, "raster", eventData, diagnostics, "event_data.raster_state",
                "m_RasterState", "m_CullingState", "rasterState", "cullingState");
            AddStateObject(state, "depth", eventData, diagnostics, "event_data.depth_state",
                "m_DepthState", "depthState");
            AddStateObject(state, "stencil", eventData, diagnostics, "event_data.stencil_state",
                "m_StencilState", "stencilState");
            AddFirstValue(state, "stencil_ref", eventData, diagnostics, "event_data.stencil_ref", false,
                "m_StencilRef", "stencilRef");
            return state;
        }

        private static Dictionary<string, object> BuildBatching(object eventData, ExtractionDiagnostics diagnostics)
        {
            var batching = new Dictionary<string, object>();
            object cause;
            if (TryGetFirstValue(eventData, diagnostics, "event_data.batch_break_cause", false,
                    out cause, "m_BatchBreakCause", "batchBreakCause"))
            {
                batching["batch_break_cause"] = SerializeSimpleValue(cause);
                string causeText = ResolveBatchBreakCauseText(cause, diagnostics);
                if (!string.IsNullOrEmpty(causeText))
                    batching["batch_break_cause_text"] = causeText;
            }

            return batching;
        }

        private static Dictionary<string, object> BuildShaderProperties(
            object shaderInfo,
            ExtractionDiagnostics diagnostics,
            int maxShaderProperties)
        {
            var properties = new Dictionary<string, object>();
            if (shaderInfo == null)
            {
                diagnostics.AddMissing("event_data.shader_info", "m_ShaderInfo|shaderInfo");
                return properties;
            }

            int remaining = maxShaderProperties;
            AddShaderPropertyCollection(properties, "floats", shaderInfo, diagnostics, ref remaining,
                "m_Floats", "floats", "Floats");
            AddShaderPropertyCollection(properties, "ints", shaderInfo, diagnostics, ref remaining,
                "m_Ints", "ints", "Ints", "m_Integers", "integers");
            AddShaderPropertyCollection(properties, "vectors", shaderInfo, diagnostics, ref remaining,
                "m_Vectors", "vectors", "Vectors");
            AddShaderPropertyCollection(properties, "matrices", shaderInfo, diagnostics, ref remaining,
                "m_Matrices", "matrices", "Matrices");
            AddShaderPropertyCollection(properties, "buffers", shaderInfo, diagnostics, ref remaining,
                "m_Buffers", "buffers", "Buffers");
            AddShaderPropertyCollection(properties, "constant_buffers", shaderInfo, diagnostics, ref remaining,
                "m_CBuffers", "m_ConstantBuffers", "constantBuffers", "ConstantBuffers");
            return properties;
        }

        private static void AddShaderPropertyCollection(
            Dictionary<string, object> properties,
            string outputKey,
            object shaderInfo,
            ExtractionDiagnostics diagnostics,
            ref int remaining,
            params string[] aliases)
        {
            if (remaining <= 0)
            {
                diagnostics.Truncated = true;
                return;
            }

            object collection;
            if (!TryGetFirstValue(shaderInfo, diagnostics, "shader_info." + outputKey, false, out collection, aliases))
                return;

            var rows = SerializeResourceCollection(collection, diagnostics, remaining, out bool truncated);
            if (rows.Count == 0)
                return;

            remaining -= rows.Count;
            diagnostics.Truncated |= truncated;
            properties[outputKey] = rows;
        }

        private static List<object> BuildShaderResourceList(
            object shaderInfo,
            ExtractionDiagnostics diagnostics,
            int maxRows,
            string logicalKey,
            params string[] aliases)
        {
            if (shaderInfo == null)
                return new List<object>();

            object collection;
            if (!TryGetFirstValue(shaderInfo, diagnostics, "shader_info." + logicalKey, false, out collection, aliases))
                return new List<object>();

            bool truncated;
            var rows = SerializeResourceCollection(collection, diagnostics, maxRows, out truncated);
            diagnostics.Truncated |= truncated;
            return rows;
        }

        private static List<object> SerializeResourceCollection(
            object collection,
            ExtractionDiagnostics diagnostics,
            int maxRows,
            out bool truncated)
        {
            truncated = false;
            var rows = new List<object>();
            IEnumerable enumerable = collection as IEnumerable;
            if (enumerable == null || collection is string)
            {
                rows.Add(SerializeValue(collection, diagnostics, 0));
                return rows;
            }

            foreach (object item in enumerable)
            {
                if (rows.Count >= maxRows)
                {
                    truncated = true;
                    break;
                }

                rows.Add(SerializeShaderResource(item, diagnostics));
            }

            return rows;
        }

        private static object SerializeShaderResource(object item, ExtractionDiagnostics diagnostics)
        {
            if (item == null)
                return null;

            if (item is UnityEngine.Object)
                return SerializeObjectReference(item);

            Type type = item.GetType();
            if (IsSimpleType(type))
                return SerializeSimpleValue(item);

            var row = new Dictionary<string, object>();
            AddFirstValue(row, "property_name", item, diagnostics, type.FullName + ".property_name", false,
                "m_Name", "name", "Name", "m_PropertyName", "propertyName", "PropertyName");
            AddFirstValue(row, "flags", item, diagnostics, type.FullName + ".flags", false,
                "m_Flags", "flags", "Flags");
            AddFirstValue(row, "is_dynamic", item, diagnostics, type.FullName + ".is_dynamic", false,
                "m_IsDynamic", "isDynamic", "IsDynamic");
            AddFirstValue(row, "is_global", item, diagnostics, type.FullName + ".is_global", false,
                "m_IsGlobal", "isGlobal", "IsGlobal");
            AddFirstValue(row, "texture_name", item, diagnostics, type.FullName + ".texture_name", false,
                "m_TextureName", "textureName", "TextureName");

            object value;
            if (TryGetFirstValue(item, diagnostics, type.FullName + ".value", false,
                    out value, "m_Value", "value", "Value", "m_Texture", "texture", "Texture"))
            {
                row["value"] = SerializeValue(value, diagnostics, 0);
            }

            if (row.Count == 0)
                return SerializeGenericObject(item, diagnostics, 1, MaximumGenericFields);

            return row;
        }

        private static object SerializeKeywordValue(object value, ExtractionDiagnostics diagnostics)
        {
            if (value == null)
                return null;

            if (value is string)
                return value;

            if (value is IEnumerable enumerable)
                return SerializeEnumerable(enumerable, diagnostics, DefaultShaderPropertyLimit);

            return SerializeValue(value, diagnostics, 0);
        }

        private static void AddStateObject(
            Dictionary<string, object> state,
            string outputKey,
            object eventData,
            ExtractionDiagnostics diagnostics,
            string logicalName,
            params string[] aliases)
        {
            object value;
            if (TryGetFirstValue(eventData, diagnostics, logicalName, false, out value, aliases))
                state[outputKey] = SerializeGenericObject(value, diagnostics, 0, MaximumGenericFields);
        }

        private static void AddSection(
            Dictionary<string, object> sections,
            List<string> availableSections,
            string key,
            Dictionary<string, object> section)
        {
            if (section == null || section.Count == 0)
                return;

            sections[key] = section;
            availableSections.Add(key);
        }

        private static void AddFirstValue(
            Dictionary<string, object> target,
            string outputKey,
            object source,
            ExtractionDiagnostics diagnostics,
            string logicalName,
            bool recordMissing,
            params string[] aliases)
        {
            object value;
            if (TryGetFirstValue(source, diagnostics, logicalName, recordMissing, out value, aliases))
                target[outputKey] = SerializeValue(value, diagnostics, 0);
        }

        private static bool TryGetFirstValue(
            object source,
            ExtractionDiagnostics diagnostics,
            string logicalName,
            bool recordMissing,
            out object value,
            params string[] aliases)
        {
            value = null;
            if (source == null)
            {
                if (recordMissing)
                    diagnostics.AddMissing(logicalName, string.Join("|", aliases));
                return false;
            }

            Type type = source.GetType();
            for (int i = 0; i < aliases.Length; i++)
            {
                bool found;
                object candidate = GetMemberValue(type, source, aliases[i], out found);
                if (!found)
                    continue;

                value = candidate;
                return value != null;
            }

            if (recordMissing)
                diagnostics.AddMissing(logicalName, type.FullName + ":" + string.Join("|", aliases));
            return false;
        }

        private static object SerializeValue(object value, ExtractionDiagnostics diagnostics, int depth)
        {
            if (value == null)
                return null;

            if (value is UnityEngine.Object)
                return SerializeObjectReference(value);

            Type type = value.GetType();
            if (IsSimpleType(type))
                return SerializeSimpleValue(value);

            if (value is Vector2 vector2)
                return new Dictionary<string, object> { ["x"] = vector2.x, ["y"] = vector2.y };

            if (value is Vector3 vector3)
                return new Dictionary<string, object> { ["x"] = vector3.x, ["y"] = vector3.y, ["z"] = vector3.z };

            if (value is Vector4 vector4)
                return new Dictionary<string, object> { ["x"] = vector4.x, ["y"] = vector4.y, ["z"] = vector4.z, ["w"] = vector4.w };

            if (value is Color color)
                return new Dictionary<string, object> { ["r"] = color.r, ["g"] = color.g, ["b"] = color.b, ["a"] = color.a };

            if (value is Rect rect)
            {
                return new Dictionary<string, object>
                {
                    ["x"] = rect.x,
                    ["y"] = rect.y,
                    ["width"] = rect.width,
                    ["height"] = rect.height,
                };
            }

            if (value is Matrix4x4 matrix)
                return SerializeMatrix(matrix);

            if (value is IEnumerable enumerable && !(value is string))
                return SerializeEnumerable(enumerable, diagnostics, DefaultShaderPropertyLimit);

            if (depth >= 2)
                return value.ToString();

            return SerializeGenericObject(value, diagnostics, depth + 1, MaximumGenericFields);
        }

        private static object SerializeSimpleValue(object value)
        {
            if (value == null)
                return null;

            Type type = value.GetType();
            if (type.IsEnum)
                return value.ToString();

            if (value is char)
                return value.ToString();

            return value;
        }

        private static List<object> SerializeEnumerable(IEnumerable enumerable, ExtractionDiagnostics diagnostics, int maxRows)
        {
            var values = new List<object>();
            if (enumerable == null)
                return values;

            foreach (object item in enumerable)
            {
                if (values.Count >= maxRows)
                {
                    diagnostics.Truncated = true;
                    break;
                }

                values.Add(SerializeValue(item, diagnostics, 1));
            }

            return values;
        }

        private static Dictionary<string, object> SerializeGenericObject(
            object value,
            ExtractionDiagnostics diagnostics,
            int depth,
            int maxFields)
        {
            var result = new Dictionary<string, object>();
            if (value == null)
                return result;

            Type type = value.GetType();
            if (IsSimpleType(type))
            {
                result["value"] = SerializeSimpleValue(value);
                return result;
            }

            int count = 0;
            foreach (FieldInfo field in type.GetFields(InstanceFlags))
            {
                if (field.IsStatic)
                    continue;

                if (count >= maxFields)
                {
                    diagnostics.Truncated = true;
                    break;
                }

                object fieldValue;
                try
                {
                    fieldValue = field.GetValue(value);
                }
                catch (Exception ex)
                {
                    diagnostics.AddWarning($"{type.FullName}.{field.Name}: {ex.Message}");
                    continue;
                }

                if (fieldValue == null)
                    continue;

                result[NormalizeMemberName(field.Name)] = SerializeValue(fieldValue, diagnostics, depth + 1);
                count++;
            }

            foreach (PropertyInfo property in type.GetProperties(InstanceFlags))
            {
                if (count >= maxFields)
                {
                    diagnostics.Truncated = true;
                    break;
                }

                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                    continue;

                object propertyValue;
                try
                {
                    propertyValue = property.GetValue(value, null);
                }
                catch
                {
                    continue;
                }

                if (propertyValue == null)
                    continue;

                string key = NormalizeMemberName(property.Name);
                if (result.ContainsKey(key))
                    continue;

                result[key] = SerializeValue(propertyValue, diagnostics, depth + 1);
                count++;
            }

            if (result.Count == 0)
                result["value"] = value.ToString();

            return result;
        }

        private static List<object> SerializeMatrix(Matrix4x4 matrix)
        {
            return new List<object>
            {
                matrix.m00, matrix.m01, matrix.m02, matrix.m03,
                matrix.m10, matrix.m11, matrix.m12, matrix.m13,
                matrix.m20, matrix.m21, matrix.m22, matrix.m23,
                matrix.m30, matrix.m31, matrix.m32, matrix.m33,
            };
        }

        private static object SerializeObjectReference(object value)
        {
            if (value == null)
                return null;

            if (value is UnityEngine.Object unityObject)
                return SerializeUnityObject(unityObject);

            if (TryConvertToInt(value, out int instanceId))
            {
                var unityObjectFromId = UnityObjectIdCompat.InstanceIDToObjectCompat(instanceId);
                var metadata = SerializeUnityObject(unityObjectFromId);
                metadata["instance_id"] = instanceId;
                return metadata;
            }

            return new Dictionary<string, object>
            {
                ["value"] = value.ToString(),
                ["type"] = value.GetType().FullName,
            };
        }

        private static Dictionary<string, object> SerializeUnityObject(UnityEngine.Object unityObject)
        {
            var metadata = new Dictionary<string, object>();
            if (unityObject == null)
                return metadata;

            metadata["instance_id"] = unityObject.GetInstanceIDCompat();
            metadata["name"] = unityObject.name;
            metadata["type"] = unityObject.GetType().FullName;

            string assetPath = AssetDatabase.GetAssetPath(unityObject);
            if (!string.IsNullOrEmpty(assetPath))
                metadata["asset_path"] = assetPath;

            if (unityObject is Texture texture)
            {
                metadata["width"] = texture.width;
                metadata["height"] = texture.height;
                metadata["dimension"] = texture.dimension.ToString();
            }

            return metadata;
        }

        private static object[] GetFrameEvents(ExtractionDiagnostics diagnostics)
        {
            if (GetFrameEventsMethod == null)
            {
                diagnostics.AddMissing("frame_debugger.get_frame_events", "GetFrameEvents");
                return null;
            }

            try
            {
                var raw = GetFrameEventsMethod.Invoke(null, null);
                if (raw is Array array)
                {
                    var values = new object[array.Length];
                    array.CopyTo(values, 0);
                    return values;
                }

                diagnostics.AddWarning("FrameDebuggerUtility.GetFrameEvents did not return an array.");
            }
            catch (Exception ex)
            {
                diagnostics.AddWarning("FrameDebuggerUtility.GetFrameEvents failed: " + UnwrapReflectionException(ex).Message);
            }

            return null;
        }

        private static object GetDescriptor(object[] frameEvents, int eventIndex)
        {
            if (frameEvents == null || eventIndex < 0 || eventIndex >= frameEvents.Length)
                return null;

            return frameEvents[eventIndex];
        }

        private static object GetEventDataOrError(int eventIndex, ExtractionDiagnostics diagnostics, out object eventData)
        {
            eventData = null;
            if (!EventDataAvailable())
            {
                return new ErrorResponse("FRAME_DEBUGGER_EVENT_DATA_UNAVAILABLE", new
                {
                    message = "FrameDebuggerUtility.GetFrameEventData is unavailable in this Unity Editor.",
                    event_index = eventIndex,
                    unity_version = Application.unityVersion,
                    missing_fields = diagnostics.MissingFields,
                    warnings = diagnostics.Warnings,
                });
            }

            if (!TryGetEventData(eventIndex, diagnostics, out eventData) || eventData == null)
            {
                return new ErrorResponse("FRAME_DEBUGGER_EVENT_DATA_UNAVAILABLE", new
                {
                    message = "FrameDebuggerUtility.GetFrameEventData did not return data for the selected event.",
                    event_index = eventIndex,
                    unity_version = Application.unityVersion,
                    missing_fields = diagnostics.MissingFields,
                    warnings = diagnostics.Warnings,
                });
            }

            return null;
        }

        private static bool TryGetEventData(int eventIndex, ExtractionDiagnostics diagnostics, out object eventData)
        {
            eventData = null;
            try
            {
                ParameterInfo[] parameters = GetEventDataMethod.GetParameters();
                if (parameters.Length == 2 && EventDataType != null)
                {
                    object data = Activator.CreateInstance(EventDataType);
                    var args = new[] { (object)eventIndex, data };
                    object ok = GetEventDataMethod.Invoke(null, args);
                    if (ok is bool boolResult && !boolResult)
                    {
                        diagnostics.AddWarning($"GetFrameEventData returned false for event {eventIndex}.");
                        return false;
                    }

                    eventData = args[1];
                    return eventData != null;
                }

                if (parameters.Length == 1)
                {
                    eventData = GetEventDataMethod.Invoke(null, new object[] { eventIndex });
                    return eventData != null;
                }

                diagnostics.AddWarning($"GetFrameEventData has unexpected parameter count: {parameters.Length}.");
                return false;
            }
            catch (Exception ex)
            {
                diagnostics.AddWarning("GetFrameEventData failed: " + UnwrapReflectionException(ex).Message);
                return false;
            }
        }

        private static string GetEventName(int eventIndex)
        {
            if (GetEventInfoNameMethod == null)
                return null;

            try
            {
                return GetEventInfoNameMethod.Invoke(null, new object[] { eventIndex }) as string;
            }
            catch
            {
                return null;
            }
        }

        private static void InvokeSetEnabled(bool value)
        {
            int paramCount = EnableMethod.GetParameters().Length;
            if (paramCount == 2)
                EnableMethod.Invoke(null, new object[] { value, 0 });
            else if (paramCount == 1)
                EnableMethod.Invoke(null, new object[] { value });
            else
                throw new InvalidOperationException($"SetEnabled has unexpected {paramCount} parameters.");
        }

        private static int GetEventCount()
        {
            try
            {
                return Convert.ToInt32(EventCountProp.GetValue(null), CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private static object ValidateEventIndex(int eventIndex)
        {
            int totalEvents = GetEventCount();
            if (totalEvents == 0)
                return NoRecordedEventsError();

            if (eventIndex < 0 || eventIndex >= totalEvents)
            {
                return new ErrorResponse("FRAME_DEBUGGER_EVENT_INDEX_OUT_OF_RANGE", new
                {
                    message = $"event_index {eventIndex} is outside the recorded Frame Debugger range.",
                    event_index = eventIndex,
                    total_events = totalEvents,
                    unity_version = Application.unityVersion,
                });
            }

            return null;
        }

        private static Result<int> GetRequiredEventIndex(ToolParams p)
        {
            int? eventIndex = p.GetInt("event_index");
            if (!eventIndex.HasValue)
                eventIndex = p.GetInt("index");

            if (!eventIndex.HasValue)
                return Result<int>.Error("'event_index' parameter is required.");

            return Result<int>.Success(eventIndex.Value);
        }

        private static Result<string> ResolveOutputPath(string requestedPath, int eventIndex)
        {
            string path = requestedPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Path.Combine(
                    Path.GetTempPath(),
                    "unity-frame-debugger",
                    "event-" + eventIndex.ToString(CultureInfo.InvariantCulture) + "-"
                    + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".png");
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return Result<string>.Error($"Invalid output_path '{requestedPath}'.");
            }

            if (!fullPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                fullPath += ".png";

            if (IsInsideAssetsDirectory(fullPath))
                return Result<string>.Error("output_path must not be inside the Unity Assets directory.");

            return Result<string>.Success(fullPath);
        }

        private static bool TryWritePng(
            string fullPath,
            byte[] pngBytes,
            int eventIndex,
            ExtractionDiagnostics diagnostics,
            out long sizeBytes,
            out object errorResponse)
        {
            sizeBytes = 0L;
            errorResponse = null;

            try
            {
                string directory = Path.GetDirectoryName(fullPath);
                Directory.CreateDirectory(string.IsNullOrEmpty(directory) ? Path.GetTempPath() : directory);
                File.WriteAllBytes(fullPath, pngBytes);
                sizeBytes = new FileInfo(fullPath).Length;
                return true;
            }
            catch (Exception ex) when (IsFileWriteException(ex))
            {
                errorResponse = new ErrorResponse("FRAME_DEBUGGER_OUTPUT_WRITE_FAILED", new
                {
                    message = ex.Message,
                    event_index = eventIndex,
                    output_path = fullPath,
                    unity_version = Application.unityVersion,
                    missing_fields = diagnostics.MissingFields,
                    warnings = diagnostics.Warnings,
                });
                return false;
            }
        }

        private static bool IsFileWriteException(Exception ex)
        {
            return ex is IOException
                   || ex is UnauthorizedAccessException
                   || ex is ArgumentException
                   || ex is NotSupportedException
                   || ex is PathTooLongException;
        }

        private static bool IsInsideAssetsDirectory(string path)
        {
            string assetsPath;
            try
            {
                assetsPath = Path.GetFullPath(Application.dataPath);
            }
            catch
            {
                return false;
            }

            string normalizedPath = NormalizeDirectoryPath(path);
            string normalizedAssetsPath = NormalizeDirectoryPath(assetsPath);
            return string.Equals(normalizedPath, normalizedAssetsPath, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedAssetsPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeDirectoryPath(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static object GetMemberValue(Type type, object source, string name, out bool found)
        {
            found = false;
            MemberInfo member = GetMember(type, name);
            if (member == null)
                return null;

            found = true;
            if (member is FieldInfo field)
                return field.GetValue(source);

            if (member is PropertyInfo property && property.CanRead && property.GetIndexParameters().Length == 0)
                return property.GetValue(source, null);

            return null;
        }

        private static MemberInfo GetMember(Type type, string name)
        {
            Dictionary<string, MemberInfo> members;
            if (!MemberCache.TryGetValue(type, out members))
            {
                members = new Dictionary<string, MemberInfo>(StringComparer.Ordinal);
                MemberCache[type] = members;
            }

            MemberInfo member;
            if (members.TryGetValue(name, out member))
                return member;

            member = (MemberInfo)type.GetField(name, InstanceFlags)
                  ?? type.GetProperty(name, InstanceFlags);

            members[name] = member;
            return member;
        }

        private static bool EventDataAvailable()
        {
            return GetEventDataMethod != null;
        }

        private static object FrameDebuggerUnavailable()
        {
            return new ErrorResponse("FRAME_DEBUGGER_UNAVAILABLE", new
            {
                message = "FrameDebuggerUtility was not found through Unity Editor reflection.",
                unity_version = Application.unityVersion,
            });
        }

        private static object NoEventsListResponse(int pageSize, int cursor, ExtractionDiagnostics diagnostics)
        {
            return new SuccessResponse("Frame Debugger has no recorded events.", new
            {
                events = new object[0],
                total_events = 0,
                page_size = pageSize,
                cursor = cursor,
                unity_version = Application.unityVersion,
                missing_fields = diagnostics.MissingFields,
                warnings = diagnostics.Warnings,
            });
        }

        private static object NoRecordedEventsError()
        {
            return new ErrorResponse("FRAME_DEBUGGER_NO_EVENTS", new
            {
                message = "Frame Debugger has no recorded events. Enable Frame Debugger and select a recorded frame before reading events.",
                unity_version = Application.unityVersion,
            });
        }

        private static string ResolveBatchBreakCauseText(object cause, ExtractionDiagnostics diagnostics)
        {
            if (!TryConvertToInt(cause, out int causeIndex))
            {
                diagnostics.AddWarning("m_BatchBreakCause was not convertible to an integer index.");
                return null;
            }

            if (GetBatchBreakCauseStringsMethod == null)
            {
                diagnostics.AddMissing(
                    "frame_debugger.batch_break_cause_strings",
                    "FrameDebuggerUtility.GetBatchBreakCauseStrings()");
                return null;
            }

            try
            {
                object rawStrings = GetBatchBreakCauseStringsMethod.Invoke(null, null);
                var strings = rawStrings as string[];
                if (strings == null)
                {
                    diagnostics.AddWarning("FrameDebuggerUtility.GetBatchBreakCauseStrings did not return string[].");
                    return null;
                }

                if (causeIndex < 0 || causeIndex >= strings.Length)
                {
                    diagnostics.AddWarning($"m_BatchBreakCause index {causeIndex} is outside GetBatchBreakCauseStrings length {strings.Length}.");
                    return null;
                }

                return strings[causeIndex];
            }
            catch (Exception ex)
            {
                diagnostics.AddWarning("FrameDebuggerUtility.GetBatchBreakCauseStrings failed: " + UnwrapReflectionException(ex).Message);
                return null;
            }
        }

        private static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive
                   || type.IsEnum
                   || type == typeof(string)
                   || type == typeof(decimal)
                   || type == typeof(DateTime)
                   || type == typeof(Guid);
        }

        private static bool TryConvertToInt(object value, out int result)
        {
            result = 0;
            if (value == null)
                return false;

            try
            {
                result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool SampleNonZeroPixels(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            if (pixels == null || pixels.Length == 0)
                return false;

            int step = Math.Max(1, pixels.Length / 2048);
            for (int i = 0; i < pixels.Length; i += step)
            {
                Color32 pixel = pixels[i];
                if (pixel.a != 0 && (pixel.r != 0 || pixel.g != 0 || pixel.b != 0))
                    return true;
            }

            return false;
        }

        private static Exception UnwrapReflectionException(Exception ex)
        {
            return ex is TargetInvocationException targetInvocationException && targetInvocationException.InnerException != null
                ? targetInvocationException.InnerException
                : ex;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            return value > max ? max : value;
        }

        private static string NormalizeMemberName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            if (name.StartsWith("m_", StringComparison.Ordinal))
                name = name.Substring(2);
            else if (name.StartsWith("_", StringComparison.Ordinal))
                name = name.Substring(1);

            var chars = new List<char>(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsUpper(c) && i > 0 && chars[chars.Count - 1] != '_')
                    chars.Add('_');
                chars.Add(char.ToLowerInvariant(c));
            }

            return new string(chars.ToArray());
        }

        private sealed class ExtractionDiagnostics
        {
            private readonly List<string> _missingFields = new List<string>();
            private readonly List<string> _warnings = new List<string>();

            public bool Truncated { get; set; }
            public IReadOnlyList<string> MissingFields => _missingFields;
            public IReadOnlyList<string> Warnings => _warnings;

            public void AddMissing(string logicalName, string aliases)
            {
                if (_missingFields.Count >= MaximumMissingFields)
                {
                    Truncated = true;
                    return;
                }

                string entry = logicalName + " (" + aliases + ")";
                if (!_missingFields.Contains(entry))
                    _missingFields.Add(entry);
            }

            public void AddWarning(string warning)
            {
                if (_warnings.Count >= MaximumWarnings)
                {
                    Truncated = true;
                    return;
                }

                if (!_warnings.Contains(warning))
                    _warnings.Add(warning);
            }
        }
    }
}
