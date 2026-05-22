import inspect
from typing import Annotated, Literal, get_args, get_origin

import pytest
from pydantic.fields import FieldInfo

from models.models import ToolDefinitionModel, ToolParameterModel
from services.custom_tool_service import CustomToolService


class _RecordingMcp:
    def __init__(self):
        self.tools = {}
        self.removed = []

    def custom_route(self, _path, methods=None):  # noqa: ARG002
        def _decorator(fn):
            return fn

        return _decorator

    def tool(self, name=None, description=None, **_kwargs):
        def _decorator(fn):
            self.tools[name] = {
                "name": name,
                "description": description,
                "fn": fn,
            }
            return fn

        return _decorator

    def remove_tool(self, name):
        self.removed.append(name)
        self.tools.pop(name, None)


def _annotated_parts(annotation):
    assert get_origin(annotation) is Annotated
    inner, field_info = get_args(annotation)
    assert isinstance(field_info, FieldInfo)
    return inner, field_info


@pytest.mark.asyncio
async def test_global_custom_tool_exposes_parameter_signature():
    mcp = _RecordingMcp()
    service = CustomToolService(mcp)

    service.register_global_tools([
        ToolDefinitionModel(
            name="custom_schema_tool",
            description="Schema test tool",
            parameters=[
                ToolParameterModel(
                    name="action",
                    description="Action to perform",
                    type="string",
                    required=True,
                    enum_values=["status", "set"],
                ),
                ToolParameterModel(
                    name="enabled",
                    description="Enable flag",
                    type="boolean",
                    required=False,
                    aliases=["enabledFlag"],
                    nullable=True,
                ),
                ToolParameterModel(
                    name="style_fields",
                    description="Resolved style fields",
                    type="array",
                    required=False,
                    items_type="string",
                ),
            ],
        )
    ])

    fn = mcp.tools["custom_schema_tool"]["fn"]
    signature = inspect.signature(fn)
    action = signature.parameters["action"]
    enabled = signature.parameters["enabled"]
    style_fields = signature.parameters["style_fields"]

    action_inner, action_field = _annotated_parts(action.annotation)
    enabled_inner, enabled_field = _annotated_parts(enabled.annotation)
    style_fields_inner, style_fields_field = _annotated_parts(style_fields.annotation)

    assert action.kind is inspect.Parameter.KEYWORD_ONLY
    assert action.default is inspect._empty
    assert get_origin(action_inner) is Literal
    assert get_args(action_inner) == ("status", "set")
    assert action_field.description == "Action to perform"
    assert enabled.kind is inspect.Parameter.KEYWORD_ONLY
    assert enabled.default is None
    assert "Enable flag" in enabled_field.description
    assert "Aliases accepted by Unity handler: enabledFlag." in enabled_field.description
    assert "bool" in str(enabled_inner)
    assert style_fields.kind is inspect.Parameter.KEYWORD_ONLY
    assert style_fields.default is None
    assert "list[str]" in str(style_fields_inner)
    assert style_fields_field.description == "Resolved style fields"


@pytest.mark.asyncio
async def test_global_custom_tool_schema_is_replaced_when_definition_changes():
    mcp = _RecordingMcp()
    service = CustomToolService(mcp)

    service.register_global_tools([
        ToolDefinitionModel(name="custom_schema_replaced", description="Old schema")
    ])
    service.register_global_tools([
        ToolDefinitionModel(
            name="custom_schema_replaced",
            description="New schema",
            parameters=[
                ToolParameterModel(
                    name="action",
                    type="string",
                    required=True,
                    enum_values=["status"],
                )
            ],
        )
    ])

    tool = mcp.tools["custom_schema_replaced"]
    signature = inspect.signature(tool["fn"])
    action = signature.parameters["action"]

    assert mcp.removed == ["custom_schema_replaced"]
    assert tool["description"] == "New schema"
    assert action.kind is inspect.Parameter.KEYWORD_ONLY
    assert action.default is inspect._empty
    assert get_origin(action.annotation) is Literal
    assert get_args(action.annotation) == ("status",)


@pytest.mark.asyncio
async def test_global_custom_tool_allows_optional_parameter_before_required_parameter():
    mcp = _RecordingMcp()
    service = CustomToolService(mcp)

    service.register_global_tools([
        ToolDefinitionModel(
            name="custom_schema_out_of_order",
            description="Out of order schema",
            parameters=[
                ToolParameterModel(
                    name="optional_first",
                    type="string",
                    required=False,
                    default_value="fallback",
                ),
                ToolParameterModel(
                    name="required_second",
                    type="string",
                    required=True,
                ),
            ],
        )
    ])

    signature = inspect.signature(mcp.tools["custom_schema_out_of_order"]["fn"])
    optional_first = signature.parameters["optional_first"]
    required_second = signature.parameters["required_second"]

    assert list(signature.parameters) == ["ctx", "optional_first", "required_second"]
    assert optional_first.kind is inspect.Parameter.KEYWORD_ONLY
    assert optional_first.default == "fallback"
    assert required_second.kind is inspect.Parameter.KEYWORD_ONLY
    assert required_second.default is inspect._empty
