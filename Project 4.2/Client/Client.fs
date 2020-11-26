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

    type Id = { id : string }
    type Error = { error : string }
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
                    Error = (fun jqXHR _ _ -> ko (System.Exception(jqXHR.ResponseText)))
                )
            ) |> ignore

    // let PostBlogArticle (article: Tweet) : Async<int> =
    //     async { let! response = Ajax "POST" "http://localhost:5000/api/tweets" article
    //             return Json.Deserialize<int> response }

    // let tweet = {id=""; reId=""; text="client"; tType="tweet"; by="deep"}
    // let response = PostBlogArticle tweet
    // printfn "response=%A" response

    // let Register =

    [<SPAEntryPoint>]
    let Main () =

        let mutable mUser = Unchecked.defaultof<User>

        let InfoText = Var.Create ""

        // User
        // Register
        let registerUser (user: User): Id Async =
            printfn "Sending registration request to server"    
            async {
                let! response = Ajax "POST" "http://localhost:5000/api/users" (Json.Serialize user)
                mUser <- user
                return Json.Deserialize<Id> response
            }
            
        // Login
        let loginUser (user: User): Id Async =
            printfn "Sending login request to server"
            async {
                let! response = Ajax "POST" "http://localhost:5000/api/users/login" (Json.Serialize user)
                mUser <- user
                return Json.Deserialize<Id> response
            }

        // Subscribe
        let subscribeTo (subscribe: Subscribe) =
            printfn "Sending subscription request to server"
            async {
                let! response = Ajax "POST" "http://localhost:5000/api/users/subscribe" (Json.Serialize subscribe)
                return Json.Deserialize response
            } |> Async.Start

        let tweets : ListModel<string, Tweet> = 
            ListModel.Create (fun tweet -> tweet.id) []
        
        let addTweet (tweet: Tweet) =
            tweets.Add(tweet)
        
        let postTweet (tweet: Tweet) : Id Async=
            printfn "Sending tweet to server"
            async {
                let! response = Ajax "POST" "http://localhost:5000/api/tweets" (Json.Serialize tweet)
                return Json.Deserialize<Id> response
            }

        let searchTweets (query: QueryTweet): Tweet list Async =
            printfn "Sending query to server"
            async {
                let! response = Ajax "POST" "http://localhost:5000/api/tweets/search" (Json.Serialize query)
                return Json.Deserialize<Tweet list> response
            }

        let getErrorMessage (error: string) =
            error.Replace("{\"error\":\"", "").Replace("\"}", "")

        // let newTweet = {id=""; reId=""; text="client"; tType="tweet"; by="deep"}
        IndexTemplate.Main()
            .Info(
                View.Map id InfoText.View
            )
            .OnRegister(fun t->
                if t.Vars.RegUsername.Value = "" || t.Vars.RegPassword.Value = "" || t.Vars.RegConfirmPassword.Value = "" then
                    InfoText.Value <- "Fields cannot be left empty"
                else if t.Vars.RegPassword.Value <> t.Vars.RegConfirmPassword.Value then
                    InfoText.Value <- "Passwords don't match"
                else
                    let newUser = {id=0; username=t.Vars.RegUsername.Value; password=t.Vars.RegPassword.Value}
                    async {
                        try    
                            let! response = registerUser newUser
                            InfoText.Value <- "Your account was created"
                            printfn "response=%A" response
                        with error ->
                            printfn "Exception Caught: %s" error.Message
                            InfoText.Value <- getErrorMessage error.Message
                    }  |> Async.Start
                    t.Vars.RegUsername.Value <- ""
                    t.Vars.RegPassword.Value <- ""
                    t.Vars.RegConfirmPassword.Value <- ""
            )
            .OnLogin(fun t->
                if t.Vars.LogUsername.Value = "" || t.Vars.LogPassword.Value = "" then do
                    InfoText.Value <- "Fields cannot be left empty"
                else
                    let user = {id=0; username=t.Vars.LogUsername.Value; password=t.Vars.LogPassword.Value}
                    async {
                        try    
                            let! response = loginUser user
                            InfoText.Value <- ("Hi " + user.username + "!")
                            printfn "response=%A" response
                        with error ->
                            printfn "Exception Caught: %s" error.Message
                            InfoText.Value <- getErrorMessage error.Message                     
                    }  |> Async.Start
                    t.Vars.LogUsername.Value <- ""
                    t.Vars.LogPassword.Value <- ""
            )
            .OnSubscribe(fun t->
                let newSubscribe = {publisher=t.Vars.Publisher.Value; subscriber=mUser.username}
                subscribeTo newSubscribe
            )
            .OnTweet(fun t ->
                if mUser = Unchecked.defaultof<User> then
                    InfoText.Value <- "Please login/register to tweet"
                else if t.Vars.TweetText.Value = "" then
                    InfoText.Value <- "You cannot post empty tweet"
                else
                    let newTweet = {id=""; reId=""; text=t.Vars.TweetText.Value; tType="tweet"; by=mUser.username}
                    async {
                        try    
                            let! response = postTweet newTweet
                            InfoText.Value <- "Your tweet was posted"
                            printfn "response=%A" response
                        with error ->
                            printfn "Exception Caught: %s" error.Message
                            InfoText.Value <- "Your tweet wasn't posted"                        
                    }  |> Async.Start               
                    t.Vars.TweetText.Value <- ""     
            )
            // .OnRetweet(fun t->
                
            // )
            .OnSearch(fun t ->
                tweets.Clear()
                let qName = t.Vars.QueryName.Value
                let newQuery =
                    if qName = "" then
                        {qType="subscription"; qName=qName; by=mUser.username}
                    else if qName.StartsWith("#") then
                        {qType="hashtag"; qName=qName; by=mUser.username}
                    else
                        {qType="mention"; qName=qName; by=mUser.username}
                async {
                    let! response = searchTweets newQuery
                    response |> List.iter tweets.Add
                }  |> Async.Start
            )
            .QueryResults(
                tweets.View.DocSeqCached(fun (tweet: Tweet) ->
                    let retweet = {id=""; reId=tweet.id; text=""; tType="retweet"; by=mUser.username}
                    IndexTemplate.Tweet()
                        .Text(tweet.text)
                        .OnRetweet(fun t->
                            async {
                                try    
                                    let! response = postTweet retweet
                                    InfoText.Value <- "You retweeted"
                                    printfn "response=%A" response
                                with error ->
                                    printfn "Exception Caught: %s" error.Message
                                    InfoText.Value <- "Your tweet wasn't posted"                        
                            }  |> Async.Start
                    ).Doc()
                )
            )
            // .TweetText(newTweet)
            
            .Doc()
        |> Doc.RunById "main"