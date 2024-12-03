from pathlib import Path
import argparse

import yaml

file_dir = Path(__file__).parent.absolute()
proj_root = file_dir.parent.parent.parent


CS_PATTERN = """{imports}

namespace {namespace};

public static class Actions
{
    public static readonly JsonSerializer Serializer = JsonSerializer.Create(JsonSettings.Settings);

    internal static string ToSnakeCase(this string str)
        => string.Concat(str.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c : c.ToString())).ToLower();
}

{classes}
"""

CLASS_PATTERN = """
public class {class_name}
{{req_fields}
    public static {class_name} Of(JObject? data) => {produce_req_expr};

    public static JObject Response({func_args}) => new JObject
    {
{produce_resp_expr_content}
    };
}"""

FILED_PATTERN = """    public {T} {field_name};"""

PRODUCE_EMPTY_REQ_EXPR_PATTERN = """new {class_name}()"""
PRODUCE_REQ_EXPR_PATTERN = """data?.ToObject<{class_name}>()!"""

RESP_JOBJECT_ENTRY_PATTERN = """        [nameof({arg_name}).ToSnakeCase()] = JToken.FromObject({arg_name}, Actions.Serializer)"""

FUNC_ARG_PATTERN = """{T} {field_name}"""

ACTION_TYPE_PATTERN = """namespace {namespace};
public enum ActionType
{
{action_types}
}"""

USING_PATTERN = """using {library};"""


def snake2camel(name: str, big=True):
    # big camel
    if big:
        return "".join([x.capitalize() for x in name.split("_")])

    # small camel
    groups = name.split("_")
    if len(groups) == 1:
        return groups[0]

    return groups[0] + "".join([x.capitalize() for x in groups[1:]])


def yml2cs(
    actions: list[dict[str, dict[str, str]]], namespace: str, outer_class_name: str
):
    _ = snake2camel

    cs = CS_PATTERN

    classes = []

    for action in actions:
        class_name, class_data = list(action.items())[0]

        request_fields = []
        # request fields
        request = class_data["req"] or []
        for field in request:
            request_fields.append(
                FILED_PATTERN.replace("{T}", request[field]).replace(
                    "{field_name}", _(field)
                )
            )
        request_fields_str = "\n".join(request_fields)
        request_fields_str = (
            f"\n{request_fields_str}\n" if request_fields else request_fields_str
        )
        produce_req_expr = (
            PRODUCE_EMPTY_REQ_EXPR_PATTERN
            if not request_fields
            else PRODUCE_REQ_EXPR_PATTERN
        )
        produce_req_expr_str = produce_req_expr.replace("{class_name}", _(class_name))

        response_fields = []
        # response fields
        response = class_data["resp"] or []
        for field in response:
            response_fields.append(
                FILED_PATTERN.replace("{T}", response[field]).replace(
                    "{field_name}", _(field)
                )
            )
        response_fields_str = "\n".join(response_fields)

        func_args = []
        produce_resp_expr_contents = []
        for field in response:
            func_args.append(
                FUNC_ARG_PATTERN.replace("{T}", response[field]).replace(
                    "{field_name}", _(field, False)
                )
            )
            produce_resp_expr_contents.append(
                RESP_JOBJECT_ENTRY_PATTERN.replace("{arg_name}", _(field, False))
            )
        func_args_str = ", ".join(func_args)
        produce_resp_expr_content_str = ",\n".join(produce_resp_expr_contents)

        # classes += class_pattern.replace('{class_name}',_(class_name)).replace('{req_fields}',request_fields).replace('{resp_fields}',response_fields)
        class_pattern_str = (
            CLASS_PATTERN.replace("{class_name}", _(class_name))
            .replace("{req_fields}", request_fields_str)
            .replace("{resp_fields}", response_fields_str)
            .replace("{func_args}", func_args_str)
            .replace("{request}", "Empty.Request" if not request_fields else "Request")
            .replace(
                "{request_ret}",
                "new Empty.Request()"
                if not request_fields
                else "Deserialize<Request>(data)",
            )
            .replace("{produce_req_expr}", produce_req_expr_str)
            .replace("{produce_resp_expr_content}", produce_resp_expr_content_str)
        )
        classes.append(class_pattern_str)

    classes_str = "".join(classes)
    return (
        cs.replace("{namespace}", namespace)
        .replace("{class_name}", _(outer_class_name))
        .replace("{classes}", classes_str)
    )


def yml2enum(actions: list[dict[str, dict[str, str]]], namespace: str):
    _ = snake2camel

    action_types = []

    for action in actions:
        class_name = list(action.keys())[0]
        action_types.append(f"    {_(class_name)}")

    action_types_str = ",\n".join(action_types)

    return ACTION_TYPE_PATTERN.replace("{namespace}", namespace).replace(
        "{action_types}", action_types_str
    )


def main():
    parser = argparse.ArgumentParser(
        description="Generate Action source code from meta file"
    )
    parser.add_argument("--meta", type=str, required=False, help="Path to meta file")
    parser.add_argument(
        "--out",
        type=str,
        required=True,
        help="Path to output file (relative to project root)",
    )

    args = parser.parse_args()
    meta_path = Path(args.meta or file_dir / "actions_meta.yml")
    out_path = Path(args.out)

    namespace = out_path.parent.as_posix().replace("/", ".")
    filename = out_path.stem

    actions = yaml.load(meta_path.read_text(), Loader=yaml.FullLoader)
    # import json
    # print(json.dumps(actions,indent=4,sort_keys=True))
    source = (
        yml2cs(actions["actions"], namespace, filename)
        .replace("{meta_path}", meta_path.relative_to(proj_root).as_posix())
        .replace(
            "{imports}",
            "\n".join(
                [USING_PATTERN.replace("{library}", x) for x in actions["imports"]]
            ),
        )
    )
    print(source)
    r = input(f"\n### Write to {out_path.as_posix()} (y/n)\n")
    if r.lower() == "y":
        p = proj_root.joinpath(out_path)
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(source, encoding="utf-8")

    enum_source = yml2enum(actions["actions"], namespace)
    print(enum_source)
    out_path_enum = out_path.parent / "ActionType.cs"
    r = input(f"\n### Write to {out_path_enum.as_posix()} (y/n)\n")
    if r.lower() == "y":
        p = proj_root.joinpath(out_path_enum)
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(enum_source, encoding="utf-8")


if __name__ == "__main__":
    main()
