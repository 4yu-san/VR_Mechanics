"""Schema for the plan the LLM produces.

The model does not write world coordinates. It writes a chain of steps, each one
placed relative to the previous piece (gap, rise, turn, shift). layout.py turns
that chain into absolute positions.
"""
from typing import Literal

from pydantic import BaseModel, Field


class Step(BaseModel):
    type: Literal["platform", "pillar", "wall", "goal"]
    width: float = Field(ge=0.5, le=60, description="size across the heading, metres")
    height: float = Field(ge=0.5, le=60, description="size in Y, metres")
    depth: float = Field(ge=0.5, le=60, description="size along the heading, metres")
    gap: float = Field(ge=0, le=60, description="empty space between the previous piece and this one, along the heading")
    rise: float = Field(ge=-30, le=30, description="change in top-surface height versus the previous piece")
    turn: Literal[-90, 0, 90] = Field(description="turn applied before moving: -90 left, 0 straight, 90 right")
    shift: float = Field(ge=-20, le=20, description="sideways offset, positive to the right of the heading")


class LevelPlan(BaseModel):
    name: str = Field(max_length=40)
    theme: str = Field(max_length=40)
    steps: list[Step] = Field(min_length=3, max_length=40)


def _inline_refs(node, defs):
    """Replace $ref entries so backends that dislike $defs still get a plain schema."""
    if isinstance(node, dict):
        if "$ref" in node:
            name = node["$ref"].split("/")[-1]
            return _inline_refs(defs[name], defs)
        return {k: _inline_refs(v, defs) for k, v in node.items() if k != "$defs"}
    if isinstance(node, list):
        return [_inline_refs(v, defs) for v in node]
    return node


def plan_json_schema() -> dict:
    schema = LevelPlan.model_json_schema()
    return _inline_refs(schema, schema.get("$defs", {}))
