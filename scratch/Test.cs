using System;
using System.Text.Json.Nodes;
using Newtonsoft.Json;
class Program {
    static void Main() {
        var node = JsonNode.Parse("{\"key\":\"value\"}");
        Console.WriteLine(JsonConvert.SerializeObject(node));
    }
}
