namespace Client
// open ClientServer
open WebSharper
// open WebSharper.JavaScript
// open WebSharper.JQuery
open WebSharper.UI
open WebSharper.UI.Client
open WebSharper.UI.Templating

module Model =

    //
    type User = {
        id: int
        username: string
        password: string
    }

    type Subscribe = {
        publisher: string
        subscriber: string
    }

    type Tweet = {
        id: string
        reId: string
        text: string
        tType: string
        by: string
    }

    type QueryTweet = {
        qType: string
        qName: string
        by: string
    }
open Model


[<JavaScript>]
module Client =
    open WebSharper.JQuery
    open WebSharper.JavaScript
    // The templates are loaded from the DOM, so you just can edit index.html
    // and refresh your browser, no need to recompile unless you add or remove holes.
    type IndexTemplate = Template<"wwwroot/index.html", ClientLoad.FromDocument>

    // let Ajax (method: string) (url: string) (serializedData: string) : Async<string> =
    //     Async.FromContinuations <| fun (ok, ko, _) ->
    //         JQuery.Ajax(
    //             AjaxSettings(
    //                 Url = url,
    //                 Type = As<JQuery.RequestType> method,
    //                 ContentType = "application/json",
    //                 DataType = DataType.Text,
    //                 Data = serializedData,
    //                 Success = (fun (result, _, _) -> ok (result :?> string)),
    //                 Error = (fun (jqXHR, _, _) -> ko (System.Exception(jqXHR.ResponseText)))))
    //         |> ignore

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
    
    

    // type RequestSettings =
    //     { RequestType : JQuery.RequestType
    //       Url : string
    //       ContentType : string option
    //       Headers : (string * string) list option
    //       Data : string option }
    //     member this.toAjaxSettings ok ko =
    //         let muable settings =
    //             JQuery.AjaxSettings
    //                 (Url = "http://localhost/api/" + this.Url, Type = this.RequestType,
    //                  DataType = JQuery.DataType.Text, Success = (fun (result, _, _) -> ok (result :?> string)),
    //                  Error = (fun (jqXHR, _, _) -> ko (System.Exception(string jqXHR.Status))))

    //         this.Headers |> Option.iter (fun h -> settings.Headers <- Object<string>(h |> Array.ofList))
    //         this.ContentType |> Option.iter (fun c -> settings.ContentType <- c)
    //         this.Data |> Option.iter (fun d -> settings.Data <- d)
    //         settings
        
    // let private ajaxCall (requestSettings : RequestSettings) =
    //     Async.FromContinuations <| fun (ok, ko, _) ->
    //         requestSettings.toAjaxSettings ok ko
    //         |> JQuery.Ajax
    //         |> ignore
    // let [<Literal>] BaseUrl = "http://localhost:5000"
    // let route = Router.Install<EndPoint>
    // let DispatchAjax (endpoint: ApiEndPoint) (parseSuccess: obj -> Message) =
    //     UpdateModel (fun state -> state)
    //     +
    //     CommandAsync (fun dispatch -> async {
    //         try
    //             // Uncomment to delay the command, to see the loading animations
    //             do! Async.Sleep 1000
    //             let! res = Promise.AsAsync <| promise {
    //                 let! ep =
    //                     EndPoint.Api (Cors.Of endpoint)
    //                     |> Router.FetchWith (Some BaseUrl) (RequestOptions()) route
    //                 return! ep.Json()
    //             }
    //             dispatch (parseSuccess res)
    //         with e ->
    //             dispatch (Error e.Message)
    //     })

    [<SPAEntryPoint>]
    let Main () =
        printfn "MAIN"
        let tweet = {id=""; reId=""; text="client"; tType="tweet"; by="deep"}
        let response = PostBlogArticle tweet
        printfn "response=%A" response
        let newName = Var.Create ""

        IndexTemplate.Main()
            // .ListContainer(
            //     People.View.DocSeqCached(fun (name: string) ->
            //         IndexTemplate.ListItem().Name(name).Doc()
            //     )
            // )
            // .Name(newName)
            // .Add(fun _ ->
            //     People.Add(newName.Value)
            //     newName.Value <- ""
            // )
            .Doc()
        |> Doc.RunById "main"