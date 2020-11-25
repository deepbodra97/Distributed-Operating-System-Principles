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
      
        // User
        // Register
        let registerUser (user: User) =
            printfn "Sending registration request to server"
            async {
                let! response = Ajax "POST" "http://localhost:5000/api/users" (Json.Serialize user)
                mUser <- user
                return Json.Deserialize response
            } |> Async.Start

        // Login
        let loginUser (user: User) =
            printfn "Sending login request to server"
            mUser <- user
            // async {
            //     let! response = Ajax "POST" "http://localhost:5000/api/users" (Json.Serialize user)
            //     mUser <- user
            //     return Json.Deserialize response
            // } |> Async.Start

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
        
        let postTweet (tweet: Tweet) =
            printfn "Sending tweet to server"
            async {
                let! response = Ajax "POST" "http://localhost:5000/api/tweets" (Json.Serialize tweet)
                return Json.Deserialize response
            } |> Async.Start

        let searchTweets (query: QueryTweet): Tweet list Async =
            printfn "Sending query to server"
            async {
                let! response = Ajax "POST" "http://localhost:5000/api/tweets/search" (Json.Serialize query)
                return Json.Deserialize<Tweet list> response
            }

        // let newTweet = {id=""; reId=""; text="client"; tType="tweet"; by="deep"}
        IndexTemplate.Main()
            .OnRegister(fun t->
                let newUser = {id=0; username=t.Vars.RegUsername.Value; password=t.Vars.RegPassword.Value}
                registerUser newUser
                t.Vars.RegUsername.Value <- ""
                t.Vars.RegPassword.Value <- ""
                t.Vars.RegConfirmPassword.Value <- ""
                // JS.Alert(++t.Vars.RegConfirmPassword.Value)
            )
            .OnLogin(fun t->
                let user = {id=0; username=t.Vars.LogUsername.Value; password=t.Vars.LogPassword.Value}
                loginUser user
            )
            .OnSubscribe(fun t->
                let newSubscribe = {publisher=t.Vars.Publisher.Value; subscriber=mUser.username}
                subscribeTo newSubscribe
            )
            .OnTweet(fun t ->
                // tweets.Add({newTweet with id=newName.Value})
                // addTweet {newTweet with id=newName.Value}
                let newTweet = {id=""; reId=""; text=t.Vars.TweetText.Value; tType="tweet"; by=mUser.username}
                postTweet newTweet
            )
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
                    response |> List.iter (fun tweet -> tweets.Add tweet)
                }  |> Async.Start
            )
            .QueryResults(
                tweets.View.DocSeqCached(fun (tweet: Tweet) ->
                    IndexTemplate.Tweet().Text(tweet.text).Doc()
                )
            )
            // .TweetText(newTweet)
            
            .Doc()
        |> Doc.RunById "main"