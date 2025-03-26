"""
从proto_type.yml中读取协议类型定义
"""

from pathlib import Path
import yaml

CONST_MAP: dict[str, str] = {}
# ============================================================
# =======================+++ ACTION +++=======================
# ===================== ACTION PARAMETERS =====================
ACTION_PARAMETERS_TEMPLATE = """namespace {ACTION_BASE_NAMESPACE}.Parameters
{
    public interface IActionParameter
    {
    }

{ACTION_PARAMETERS}
}"""
EMPTY_ACTION_PARAMETER_RECORD \
    = """    public sealed record EmptyActionParameter : IActionParameter;"""

ACTION_PARAMETER = """    public sealed record {ACTION_TYPE}Parameter : IActionParameter
    {
{RECORD_BODY}
    }"""

EMPTY_ACTION_PARAMETER = (
    """    public sealed record {ACTION_TYPE}Parameter : IActionParameter;"""
)
# ===================== ACTION PARAMETERS =====================

# ===================== ACTION RESULTS =====================
ACTION_RESULTS_TEMPLATE = """namespace {ACTION_BASE_NAMESPACE}.Results
{
    public interface IActionResult
    {
    }

{ACTION_RESULTS}
}"""

EMPTY_ACTION_RESULT_RECORD = """    public sealed record EmptyActionResult : IActionResult;"""

ACTION_RESULT = """    public sealed record {ACTION_TYPE}Result : IActionResult
    {
{RECORD_BODY}
    }"""

EMPTY_ACTION_RESULT = (
    """    public sealed record {ACTION_TYPE}Result : IActionResult;"""
)
# ===================== ACTION RESULTS =====================

# ===================== ACTION TYPE =====================
ACTION_TYPE_ENUM = """namespace {ACTION_BASE_NAMESPACE}
{
    public enum ActionType
    {
{BODY}
    }
}"""
# ===================== ACTION TYPE =====================
# =====================+++ ACTION +++===================
# =======================================================

# ===================== EVENT TYPE =====================
EVENT_TYPE_ENUM = """namespace {EVENT_BASE_NAMESPACE}
{
    public enum EventType
    {
{BODY}
    }
}"""
# ===================== EVENT TYPE =====================

# ===================== EVENT METAS =====================
EVENT_METAS_TEMPLATE = """namespace {EVENT_BASE_NAMESPACE}.Meta
{
    public interface IEventMeta
    {
    }

{EVENT_META_LIST}
}"""

EVENT_META = """    public sealed record {EVENT_TYPE}EventMeta : IEventMeta
    {
{RECORD_BODY}
    }"""
EMPTY_EVENT_META = "    public sealed record {EVENT_TYPE}EventMeta : IEventMeta;"
# ===================== EVENT METAS =====================

# ===================== EVENT DATA =====================
EVENT_DATA_TEMPLATE = """namespace {EVENT_BASE_NAMESPACE}.Meta
{
    public interface IEventData
    {
    }

{EVENT_DATA_LIST}
}"""

EVENT_DATA = """    public sealed record {EVENT_TYPE}EventData : IEventData
    {
{RECORD_BODY}
    }"""
EMPTY_EVENT_DATA = "    public sealed record {EVENT_TYPE}EventData : IEventData;"
# ===================== EVENT DATA =====================


# ===================== MISC =====================
FUNC_ARG = """{PARAM_TYPE} {PARAM_NAME}"""
ENUM_BODY = """        {TYPE},"""
RECORD_BODY_ENTRY = """        public {ENTRY_TYPE} {ENTRY_NAME} { get; init; }"""
# ===================== MISC =====================

HEADER = """// This file is automatically generated.
// DO NOT EDIT THIS FILE!
"""


class StrHelper:
    """
    str helper
    """

    @staticmethod
    def snake2camel(name: str, big=True) -> str:
        """
        snake_case转camelCase或PascalCase
        """

        # big camel
        if big:
            return "".join([x.capitalize() for x in name.split("_")])

        # small camel
        groups = name.split("_")
        if len(groups) == 1:
            return groups[0]

        return groups[0] + "".join([x.capitalize() for x in groups[1:]])

    @staticmethod
    def format(template: str, **kwargs: str) -> str:
        """
        格式化模板
        """
        for k, v in kwargs.items():
            template = template.replace("{" + k.upper() + "}", v)
        return template


def is_use_empty_record() -> bool:
    """
    是否跳过空body的类型
    """
    return CONST_MAP["use_empty_record"]


def __add_imports(imports: list[str] | None, body: str) -> str:
    if not imports:
        return body

    return "\n\n".join(["\n".join(imports), body])


def __get_record_types(data_list: list[dict[str, dict[str, str]]]) -> list[str]:
    type_tokens = [list(action_def.keys())[0] for action_def in data_list]
    return [StrHelper.snake2camel(type_token) for type_token in type_tokens]


def __gen_action_enum(actions: list[dict[str, dict[str, str]]]) -> str:
    """
    从yaml生成actions相关的枚举定义
    """
    types = __get_record_types(actions)
    body = "\n".join([StrHelper.format(ENUM_BODY, TYPE=type) for type in types])
    return StrHelper.format(
        ACTION_TYPE_ENUM,
        BODY=body,
        ACTION_BASE_NAMESPACE=CONST_MAP["action_base_namespace"],
    )


def __gen_action_parameters(params_def: list[tuple[str, dict[str, str]] | None]) -> str:
    parameter_records = []
    if is_use_empty_record():
        parameter_records.append(EMPTY_ACTION_PARAMETER_RECORD)

    for type_token, action_param_def in params_def:
        parameter_record: str
        if action_param_def is None:
            if is_use_empty_record():
                continue

            parameter_record = StrHelper.format(
                EMPTY_ACTION_PARAMETER,
                ACTION_TYPE=StrHelper.snake2camel(type_token),
            )
        else:
            parameter_record = StrHelper.format(
                ACTION_PARAMETER,
                ACTION_TYPE=StrHelper.snake2camel(type_token),
                RECORD_BODY = __get_record_body(action_param_def),
            )

        parameter_records.append(parameter_record)

    return StrHelper.format(
        ACTION_PARAMETERS_TEMPLATE,
        ACTION_PARAMETERS="\n\n".join(parameter_records),
        ACTION_BASE_NAMESPACE=CONST_MAP["action_base_namespace"],
    )


def __gen_action_results(results_def: list[tuple[str, dict[str, str]] | None]) -> str:
    records = []
    if is_use_empty_record():
        records.append(EMPTY_ACTION_RESULT_RECORD)

    for type_token, record_yml_def in results_def:
        result_record: str
        if record_yml_def is None:
            if is_use_empty_record():
                continue

            result_record = StrHelper.format(
                EMPTY_ACTION_RESULT,
                ACTION_TYPE=StrHelper.snake2camel(type_token),
            )
        else:
            result_record = StrHelper.format(
                ACTION_RESULT,
                ACTION_TYPE=StrHelper.snake2camel(type_token),
                RECORD_BODY = __get_record_body(record_yml_def),
            )

        records.append(result_record)

    return StrHelper.format(
        ACTION_RESULTS_TEMPLATE,
        ACTION_RESULTS="\n\n".join(records),
        ACTION_BASE_NAMESPACE=CONST_MAP["action_base_namespace"],
    )


def gen_actions(actions: list[dict[str, dict[str, str]]]) -> str:
    """
    从yaml生成actions相关的协议类型定义
    """
    params_def = []
    results_def = []
    for action_def in actions:
        type_token = list(action_def.keys())[0]
        action_req_def = action_def[type_token]["req"]
        action_resp_def = action_def[type_token]["resp"]

        params_def.append((type_token, action_req_def))
        results_def.append((type_token, action_resp_def))

    enum = __gen_action_enum(actions)
    parameters = __gen_action_parameters(params_def)
    results = __gen_action_results(results_def)

    action_source_body = "\n\n".join([enum, parameters, results])

    return __add_imports(CONST_MAP["action_imports"], action_source_body)


def __gen_event_enum(events: list[dict[str, dict[str, str]]]) -> str:
    """
    从yaml生成actions相关的枚举定义
    """
    types = __get_record_types(events)
    body = "\n".join([StrHelper.format(ENUM_BODY, TYPE=type) for type in types])
    return StrHelper.format(
        EVENT_TYPE_ENUM,
        BODY=body,
        EVENT_BASE_NAMESPACE=CONST_MAP["event_base_namespace"],
    )


def __gen_event_metas(meta_def_list: list[tuple[str, dict[str, str]]]) -> str:
    meta_records = []

    for type_token, event_meta_def in meta_def_list:
        if event_meta_def is None:
            if is_use_empty_record():
                continue
            meta_records.append(
                StrHelper.format(
                    EMPTY_EVENT_META,
                    EVENT_TYPE=StrHelper.snake2camel(type_token),
                )
            )
        else:
            meta_records.append(
                StrHelper.format(
                    EVENT_META,
                    EVENT_TYPE=StrHelper.snake2camel(type_token),
                    RECORD_BODY = __get_record_body(event_meta_def),
                )
            )

    return StrHelper.format(
        EVENT_METAS_TEMPLATE,
        EVENT_META_LIST="\n\n".join(meta_records),
        EVENT_BASE_NAMESPACE=CONST_MAP["event_base_namespace"],
    )


def __gen_event_data(data_def_list: list[tuple[str, dict[str, str]]]) -> str:
    data_records = []

    for type_token, event_data_def in data_def_list:
        if event_data_def is None:
            if is_use_empty_record():
                continue

            data_records.append(
                StrHelper.format(
                    EMPTY_EVENT_DATA,
                    EVENT_TYPE=StrHelper.snake2camel(type_token),
                )
            )
        else:
            data_records.append(
                StrHelper.format(
                    EVENT_DATA,
                    EVENT_TYPE=StrHelper.snake2camel(type_token),
                    RECORD_BODY = __get_record_body(event_data_def),
                )
            )

    return StrHelper.format(
        EVENT_DATA_TEMPLATE,
        EVENT_DATA_LIST="\n\n".join(data_records),
        EVENT_BASE_NAMESPACE=CONST_MAP["event_base_namespace"],
    )


def gen_events(events: list[dict[str, dict[str, str]]]) -> str:
    """
    从yaml生成events相关的协议类型定义
    """
    meta_def_list = []
    data_def_list = []

    for event_def in events:
        type_token = list(event_def.keys())[0]
        event_meta_def = event_def[type_token]["meta"]
        event_data_def = event_def[type_token]["data"]

        meta_def_list.append((type_token, event_meta_def))
        data_def_list.append((type_token, event_data_def))

    enum = __gen_event_enum(events)
    metas = __gen_event_metas(meta_def_list)
    data = __gen_event_data(data_def_list)

    event_source_body = "\n\n".join([enum, metas, data])

    return __add_imports(CONST_MAP["event_imports"], event_source_body)


def init_consts(yaml_content: any) -> None:
    """
    初始化常数
    """
    CONST_MAP.update(yaml_content["const"])

def __get_record_body(record_yml_def: dict[str, str]) -> str:
    body_entries = []
    for param_name, param_type in record_yml_def.items():
        body_entries.append(
            StrHelper.format(
                RECORD_BODY_ENTRY,
                ENTRY_TYPE=param_type,
                ENTRY_NAME=StrHelper.snake2camel(param_name),
            )
        )
        if not param_type.endswith("?"):
            body_entries[-1] = body_entries[-1].replace("        " , "        [JsonRequired] ")
    return "\n".join(body_entries)

def main():
    """
    main函数
    """
    proto_type_yaml_content = (Path(__file__).parent / "proto_type.yml").read_text(
        "utf-8"
    )
    proto_type_yaml = yaml.load(proto_type_yaml_content, Loader=yaml.FullLoader)

    init_consts(proto_type_yaml)

    actions = gen_actions(proto_type_yaml["actions"])
    events = gen_events(proto_type_yaml["events"])
    print("+++++++++++++++++++++++++++++++ ACTION.CS +++++++++++++++++++++++++++++++")
    print(HEADER + actions)
    print("+++++++++++++++++++++++++++++++ ACTION.CS +++++++++++++++++++++++++++++++")
    print()
    print("+++++++++++++++++++++++++++++++ EVENT.CS +++++++++++++++++++++++++++++++")
    print(HEADER + events)
    print("+++++++++++++++++++++++++++++++ EVENT.CS +++++++++++++++++++++++++++++++")


if __name__ == "__main__":
    main()
