#r "nuget: WebSharper"
#r "nuget: WebSharper.FSharp"
#r "nuget: WebSharper.JQuery"
open WebSharper
open WebSharper.FSharp
open WebSharper.JQuery

type Tweet = {
    id: string
    reId: string
    text: string
    tType: string
    by: string
}

let Ajax (method: string) (url: string) (serializedData: Tweet) : Async<string> =
            Async.FromContinuations <| fun (ok, ko, _) ->
                
                AjaxSettings(
                    Url = url,
                    Type = As<JQuery.RequestType> method,
                    ContentType = As<Union<bool, string>>"application/json",
                    DataType = DataType.Text,
                    Data = serializedData,
                    Success = (fun result _  _ -> ok (result :?> string)),
                    Error = (fun jqXHR _ _ -> ko (System.Exception("Error")))
                ) |> JQuery.Ajax
                // JQuery.Ajax(ajaxSetting)
                |> ignore

let PostBlogArticle (article: Tweet) : Async<int> =
    async { let! response = Ajax "POST" "http://localhost:5000/api/tweets" article
            return Json.Deserialize<int> response }

let tweet = {id=""; reId=""; text="client"; tType="tweet"; by="deep"}
let response = PostBlogArticle tweet
printfn "response=%A" response