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

    type IndexTemplate = Template<"wwwroot/index.html", ClientLoad.FromDocument>

    let Ajax (method: string) (url: string) (serializedData: string) : Async<string> =
            Async.FromContinuations <| fun (ok, ko, _) ->
            JQuery.Ajax (    
                AjaxSettings(
                    Url = url,
                    Type = As<JQuery.RequestType> method,
                    ContentType = As<Union<bool, string>>"application/json",
                    DataType = DataType.Text,
                    Data = serializedData,
                    Success = (fun result _  _ -> ok (result :?> string)),
                    Error = (fun jqXHR _ _ -> ko (System.Exception("Error")))
                )
            )
                // JQuery.Ajax(ajaxSetting)
                |> ignore

    // let PostBlogArticle (article: Tweet) : Async<int> =
    //     async { let! response = Ajax "POST" "http://localhost:5000/api/tweets" article
    //             return Json.Deserialize<int> response }

    // let tweet = {id=""; reId=""; text="client"; tType="tweet"; by="deep"}
    // let response = PostBlogArticle tweet
    // printfn "response=%A" response

    // let Register =

    [<SPAEntryPoint>]
    let Main () =
        printfn "MAIN"
        
        let newTweetTextInput = Var.Create ""
        
        let tweets : ListModel<string, Tweet> = 
            ListModel.Create (fun tweet -> tweet.id) []
        
        let addTweet (tweet: Tweet) =
            tweets.Add(tweet)
        
        let postTweetToServer (tweet: Tweet) =
            printfn "Sending tweet to server"
            async {
                let! response = Ajax "POST" "http://localhost:5000/api/tweets" (Json.Serialize tweet)
                return Json.Deserialize response
            } |> Async.Start

        // let newTweet = {id=""; reId=""; text="client"; tType="tweet"; by="deep"}
        IndexTemplate.Main()
            .Register(fun t->
                JS.Alert(t.Vars.RegUsername.Value+t.Vars.RegPassword.Value+t.Vars.RegConfirmPassword.Value)
            )
            .Login(fun t->
                JS.Alert(t.Vars.LogUsername.Value+t.Vars.LogPassword.Value)
            )
            .ListContainer(
                tweets.View.DocSeqCached(fun (tweet: Tweet) ->
                    IndexTemplate.ListItem().Id(tweet.id).Text(tweet.text).Doc()
                )
            )
            // .TweetText(newTweet)
            .OnTweet(fun newTweetText ->
                // tweets.Add({newTweet with id=newName.Value})
                // addTweet {newTweet with id=newName.Value}
                let newTweet = {id=""; reId=""; text=newTweetTextInput.Value; tType="tweet"; by="Deep"}
                postTweetToServer newTweet
                newTweetTextInput.Value <- ""
            )
            .Doc()
        |> Doc.RunById "main"