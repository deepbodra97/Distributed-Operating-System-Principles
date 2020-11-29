namespace Client
open WebSharper
open WebSharper.UI
open WebSharper.UI.Client
open WebSharper.UI.Templating

module Model =
    // Message types for Request and Response
    type User = { // User
        id: int
        username: string
        password: string
    }

    type Subscribe = { // subscriber subscribes to a publisher
        publisher: string
        subscriber: string
    }

    type Tweet = { // tweet
        id: string
        reId: string
        text: string
        tType: string
        by: string
    }

    type QueryTweet = { // search for tweets
        qType: string // "subscription" | "hashtag" | "mention"
        qName: string // "" | "#tag_name" | "@username"
        by: string // user who is querying
    }

    type Id = { id : string }
    type Error = { error : string } // error type
open Model


[<JavaScript>]
module Client =
    open WebSharper.JQuery
    open WebSharper.JavaScript

    type IndexTemplate = Template<"wwwroot/index.html", ClientLoad.FromDocument> // load html template

    // To make calls to the API
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

    [<SPAEntryPoint>]
    let Main () =

        let mutable mUser = Unchecked.defaultof<User>

        let InfoText = Var.Create "" // to display any error or success information
        let tweets : ListModel<string, Tweet> = // load tweets dynamically. reactive webpage
            ListModel.Create (fun tweet -> tweet.id) []
        
        let feeds : ListModel<string, Tweet> = // load feeds dynamically. reactive webpage
            ListModel.Create (fun tweet -> tweet.id) []

        // User
        // Register
        let registerUser (user: User): Id Async =
            printfn "Sending registration request to server"    
            async {
                tweets.Clear()
                feeds.Clear()
                let! response = Ajax "POST" "http://localhost:5000/api/users" (Json.Serialize user)
                mUser <- user
                return Json.Deserialize<Id> response
            }
            
        // Login
        let loginUser (user: User): Id Async =
            printfn "Sending login request to server"
            async {
                tweets.Clear()
                feeds.Clear()
                let! response = Ajax "POST" "http://localhost:5000/api/users/login" (Json.Serialize user)
                mUser <- user
                return Json.Deserialize<Id> response
            }

        // Subscribe
        let subscribeTo (subscribe: Subscribe) : Id Async =
            printfn "Sending subscription request to server"
            async {
                let! response = Ajax "POST" "http://localhost:5000/api/users/subscribe" (Json.Serialize subscribe)
                return Json.Deserialize<Id> response
            }

        // async tasks for ajax calls
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
        
        let getFeeds (query: QueryTweet) =
            printfn "Requesting feeds"
            async {
                let! response = Ajax "POST" "http://localhost:5000/api/tweets/search" (Json.Serialize query)
                return Json.Deserialize<Tweet list> response
            }

        let getErrorMessage (error: string) =
            error.Replace("{\"error\":\"", "").Replace("\"}", "")

        JS.SetInterval (fun ()-> // fetches subscription in real time
            if mUser <> Unchecked.defaultof<User> then
                let query = {qType="subscription"; qName=""; by=mUser.username}
                async {
                    try    
                        let! response = getFeeds query
                        response |> List.iter feeds.Add
                    with error ->
                        printfn "Exception Caught: %s" error.Message
                        InfoText.Value <- (getErrorMessage error.Message)
                }  |> Async.Start
        ) 3000 |> ignore

        JS.SetInterval (fun ()-> // fetches mentions in real time
            if mUser <> Unchecked.defaultof<User> then
                let query = {qType="mention"; qName="@"+mUser.username; by=mUser.username}
                async {
                    try    
                        let! response = getFeeds query
                        response |> List.iter feeds.Add
                    with error ->
                        printfn "Exception Caught: %s" error.Message
                        InfoText.Value <- (getErrorMessage error.Message)
                }  |> Async.Start
        ) 3000 |> ignore

        IndexTemplate.Main()
            .Info(
                View.Map id InfoText.View // binds the F# variable to the dom element
            )
            .OnRegister(fun t-> // Register
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
                            InfoText.Value <- (getErrorMessage error.Message)
                    }  |> Async.Start
                    t.Vars.RegUsername.Value <- ""
                    t.Vars.RegPassword.Value <- ""
                    t.Vars.RegConfirmPassword.Value <- ""
            )
            .OnLogin(fun t-> // Login
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
                if mUser = Unchecked.defaultof<User> then
                    InfoText.Value <- "Please login/register"
                else if t.Vars.Publisher.Value = "" then
                    InfoText.Value <- "Field cannot be left blank"
                else
                    let newSubscribe = {publisher=t.Vars.Publisher.Value; subscriber=mUser.username}
                    async {
                        try    
                            let! response = subscribeTo newSubscribe
                            InfoText.Value <- "You subscribed to " + t.Vars.Publisher.Value
                            printfn "response=%A" response
                        with error ->
                            printfn "Exception Caught: %s" error.Message
                            InfoText.Value <- getErrorMessage error.Message                        
                    }  |> Async.Start               
                    t.Vars.Publisher.Value <- ""    
                    
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
                            InfoText.Value <- getErrorMessage error.Message                        
                    }  |> Async.Start               
                    t.Vars.TweetText.Value <- ""     
            )
            .OnSearch(fun t -> // query for tweets
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
            .QueryResults( // update the result of the query
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
                                    InfoText.Value <- getErrorMessage error.Message                        
                            }  |> Async.Start
                    ).Doc()
                )
            )
            .Feeds( // update the feed section
                feeds.View.DocSeqCached(fun (tweet: Tweet) ->
                    let retweet = {id=""; reId=tweet.id; text=""; tType="retweet"; by=mUser.username}
                    IndexTemplate.Feed()
                        .Text(tweet.text)
                        .OnRetweet(fun t->
                            async {
                                try    
                                    let! response = postTweet retweet
                                    InfoText.Value <- "You retweeted"
                                    printfn "response=%A" response
                                with error ->
                                    printfn "Exception Caught: %s" error.Message
                                    InfoText.Value <- getErrorMessage error.Message                        
                            }  |> Async.Start
                    ).Doc()
                )    
            )            
            .Doc()
        |> Doc.RunById "main"