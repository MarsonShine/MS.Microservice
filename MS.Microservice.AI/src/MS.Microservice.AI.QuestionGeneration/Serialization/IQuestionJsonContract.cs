using System.Text.Json;

namespace MS.Microservice.AI.QuestionGeneration.Serialization;

public interface IQuestionJsonContract
{
    JsonElement GetStrictSchema(Type responseType);

    string Serialize(object value);

    object Deserialize(string response, Type responseType);
}
