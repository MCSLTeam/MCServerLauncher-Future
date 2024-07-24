from pathlib import Path
import argparse

import yaml

file_dir = Path(__file__).parent.absolute()
proj_root = file_dir.parent.parent.parent


cs_pattern = """using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace {namespace}
{
    /// <summary>
    /// Generated by "{meta_path}"
    /// </summary>
    internal static class {class_name}
    {
        public static T Deserialize<T>(JObject data)
        {
            return JsonConvert.DeserializeObject<T>(data.ToString(), JsonService.Settings);
        }

        internal class Empty
        {
            public class Request {}

            public class Response {}

            public static Request RequestOf(JObject data)
            {
                return new Request();
            }

            public static Response ResponseOf(Guid FileId)
            {
                return new Response();
            }
        }
{classes}
    }

    /// <summary>
    /// Enum 转换器, 使枚举字面值(BigCamelCase)与json(snake_case)互转
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class SnakeCaseEnumConverter<T> : JsonConverter where T : struct, Enum
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var pascalCase = value!.ToString();
            var snakeCase = ConvertPascalCaseToSnakeCase(pascalCase);
            writer.WriteValue(snakeCase);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                var snakeCase = reader.Value!.ToString();
                var pascalCase = ConvertSnakeCaseToPascalCase(snakeCase);
                if (Enum.TryParse(pascalCase, out T result))
                {
                    return result;
                }
            }

            throw new JsonSerializationException($"Cannot convert {reader.Value} to {typeof(T)}");
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(T);
        }

        private static string ConvertSnakeCaseToPascalCase(string snakeCase)
        {
            return string.Join(string.Empty,
                snakeCase.Split('_').Select(s => char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant()));
        }

        private static string ConvertPascalCaseToSnakeCase(string pascalCase)
        {
            return string.Concat(pascalCase.Select((x, i) =>
                i > 0 && char.IsUpper(x) ? "_" + x.ToString().ToLowerInvariant() : x.ToString().ToLowerInvariant()));
        }
    }
    
    /// <summary>
    /// 解析 Guid,若字符串解析失败则返回 Guid.Empty,方便带上下文的异常检查
    /// </summary>
    internal class GuidJsonConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(value!.ToString());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                var str = reader.Value!.ToString();

                return Guid.TryParse(str, out var result) ? result : Guid.Empty;
            }

            throw new JsonSerializationException($"Cannot convert {reader.Value} to Guid");
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Guid);
        }
    }
}"""

class_pattern = """
        public class {class_name}
        {
            public class Request
            {
{req_fields}
            }

            public class Response
            {
{resp_fields}
            }

            public static Request RequestOf(JObject data)
            {
                return Deserialize<Request>(data);
            }

            public static Response ResponseOf({func_args})
            {
                return new Response { {struct_decl}  };
            }
        }
"""

field_pattern = """                public {T} {field_name};"""

func_arg_pattern = """{T} {field_name}"""
struct_decl_pattern = """{field_name} = {arg_name}"""

action_type_pattern = """namespace {namespace}
{
    internal enum ActionType
    {
        Message,
        Ping,
{action_types}
    }
}"""


def snake2big_camel(name:str):
    return ''.join([x.capitalize() for x in name.split('_')])

def yml2cs(actions:list[dict[str,dict[str,str]]], namespace:str, outer_class_name:str):
    _ = snake2big_camel

    cs = cs_pattern

    classes = []

    for action in actions:
        class_name, class_data = list(action.items())[0]

        request_fields = []
        # request fields
        request = class_data['req'] or []
        for field in request:
            request_fields.append(field_pattern.replace('{T}',request[field]).replace('{field_name}',_(field)))
        request_fields_str = '\n'.join(request_fields)


        response_fields = []
        # response fields
        response = class_data['resp'] or []
        for field in response:
            response_fields.append(field_pattern.replace('{T}',response[field]).replace('{field_name}',_(field)))
        response_fields_str = '\n'.join(response_fields)

        func_args = []
        struct_decls = []
        for field in response:
            func_args.append(func_arg_pattern.replace('{T}',response[field]).replace('{field_name}',_(field)))
            struct_decls.append(struct_decl_pattern.replace('{field_name}',_(field)).replace('{arg_name}',_(field)))
        func_args_str = ', '.join(func_args)
        struct_decls_str = ', '.join(struct_decls)

        # classes += class_pattern.replace('{class_name}',_(class_name)).replace('{req_fields}',request_fields).replace('{resp_fields}',response_fields)
        classes.append(class_pattern.replace('{class_name}',_(class_name)).replace('{req_fields}',request_fields_str).replace('{resp_fields}',response_fields_str).replace('{func_args}',func_args_str).replace('{struct_decl}',struct_decls_str))
    
    classes_str = ''.join(classes)
    return cs.replace('{namespace}',namespace).replace('{class_name}',_(outer_class_name)).replace('{classes}',classes_str)

def yml2enum(actions:list[dict[str,dict[str,str]]], namespace:str):
    _ = snake2big_camel

    action_types = []

    for action in actions:
        class_name = list(action.keys())[0]
        action_types.append(f"        {_(class_name)}")

    action_types_str = ',\n'.join(action_types)

    return action_type_pattern.replace('{namespace}',namespace).replace('{action_types}',action_types_str)



def main():
    parser = argparse.ArgumentParser(description='Generate Action source code from meta file')
    parser.add_argument('--meta', type=str,required=False ,help='Path to meta file')
    parser.add_argument('--out', type=str,required=True ,help='Path to output file (relative to project root)')

    args = parser.parse_args()
    meta_path = Path(args.meta or file_dir / 'actions_meta.yml')
    out_path = Path(args.out)

    namespace = out_path.parent.as_posix().replace('/','.')
    filename = out_path.stem

    actions = yaml.load(meta_path.read_text(), Loader=yaml.FullLoader)
    # import json
    # print(json.dumps(actions,indent=4,sort_keys=True))
    source = yml2cs(actions["actions"],namespace,filename).replace("{meta_path}",meta_path.relative_to(proj_root).as_posix())
    print(source)
    r = input(f"\n### Write to {out_path.as_posix()} (y/n)\n")
    if r.lower() == 'y':
        p = proj_root.joinpath(out_path)
        p.parent.mkdir(parents=True,exist_ok=True)
        p.write_text(source,encoding='utf-8')

    enum_source = yml2enum(actions["actions"],namespace)
    print(enum_source)
    out_path_enum = out_path.parent / "ActionType.cs"
    r = input(f"\n### Write to {out_path_enum.as_posix()} (y/n)\n")
    if r.lower() == 'y':
        p = proj_root.joinpath(out_path_enum)
        p.parent.mkdir(parents=True,exist_ok=True)
        p.write_text(enum_source,encoding='utf-8')


if __name__ == '__main__':
    main()