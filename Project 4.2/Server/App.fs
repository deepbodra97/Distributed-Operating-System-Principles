module ClientServer.App

open System
open System.Collections.Generic
open WebSharper
open WebSharper.Sitelets

open System.Text.RegularExpressions

// The message types for request and response
module Model =

    type User = {
        id: int
        username: string
        password: string
    }

    type Subscribe = { // subscriber subscribes to a publisher
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

    type QueryTweet = { // search for tweets
        qType: string // "subscription" | "hashtag" | "mention"
        qName: string // "" | "#tag_name" | "@username"
        by: string // user who is querying
    }

    // API Endpoints
    type ApiEndPoint =

        // For User
        | [<EndPoint "POST /users"; Json "User">] // Register
            CreateUser of User: User

        | [<EndPoint "POST /users/login"; Json "User">]
            LoginUser of User: User

        | [<EndPoint "POST /users/subscribe"; Json "Subscribe">]
            SubscribeTo of Subscribe: Subscribe

        // For Tweets 
        | [<EndPoint "POST /tweets"; Json "Tweet">] // post tweet
            CreateTweet of Tweet: Tweet
        | [<EndPoint "POST /tweets/search"; Json "QueryTweet">] // search tweets
            QueryTweet of QueryTweet: QueryTweet

    // The type of all endpoints for the application.
    type EndPoint =
        // Accepts requests to /
        | [<EndPoint "/">] Home

        // Accepts requests to /api/...
        | [<EndPoint "/api">] Api of Cors<ApiEndPoint>

    /// Error result value.
    type Error = { error : string }

    // generic response type
    type ApiResult<'T> = Result<'T, Http.Status * Error>

    type Id = { id : string } // to return id as response
open Model


module Utils =
    let getRandomString n = // generate random alphnumeric string of length n
        let rnd = System.Random()
        let chars = Array.concat([[|'a' .. 'z'|];[|'A' .. 'Z'|];[|'0' .. '9'|]])
        let sz = Array.length chars in
        System.String(Array.init n (fun _ -> chars.[rnd.Next sz]))
    
    let regexHashTag = "#[A-Za-z0-9]+" // regex for hashtag
    let regexMention = "@[A-Za-z0-9]+" // regex for mention

    let (|Regex|_|) pattern input =
        let matches = Regex.Matches(input, pattern)
        Some([ for m in matches -> m.Value])
    
    let getPatternMatches regex tweet =
        match tweet with
        | Regex regex tags -> tags
        | _ -> []
open Utils

// in memory database
module Backend =

    let private usersMap = new Dictionary<string, User>()

    let private tweetsMap = new Dictionary<string, Tweet>() // tweetId -> Tweet
    let private tweetsByUsername = new Dictionary<string, List<string>>() // username -> tweetId
    let private tweetsByHashTag = new Dictionary<string, List<string>>() // hashtag -> tweetId
    let private tweetsByMention = new Dictionary<string, List<string>>() // mention -> tweetId
    let private subscriptionsMap = new Dictionary<string, HashSet<string>>() // subscriptions of a given user
    let private subscribersMap = new Dictionary<string,  List<string>>()

    let maxLenTweetId = 3

    // Error responses
    let userNotFound() : ApiResult<'T> =
        Error (Http.Status.NotFound, { error = "Please register first" })
    
    let userAlreadyExists() : ApiResult<'T> =
        Error (Http.Status.NotFound, { error = "User already exists" })
    
    let wrongPassword() : ApiResult<'Ts> =
        Error (Http.Status.NotFound, { error = "Seems like you forgot your password or someone hacked your account ;)" })
    
    let userAlreadySubscribed() : ApiResult<'T> =
        Error (Http.Status.NotFound, { error = "User already subscribed" })

    // Create User
    let CreateUser (data: User) : ApiResult<Id> =
        lock usersMap <| fun () ->
            match usersMap.TryGetValue(data.username) with
            | true, _ ->
                printfn "UserCreationError: User already exists"
                userAlreadyExists()
            | false, _ ->
                usersMap.[data.username] <- data
                Ok { id =  data.username}
    
    let LoginUser (data: User) : ApiResult<Id> =
        lock usersMap <| fun () ->
            match usersMap.TryGetValue(data.username) with
            | true, user ->
                if user.password = data.password then
                    Ok { id =  data.username}
                else
                    wrongPassword()
            | false, _ ->
                userNotFound()
    
    let SubscribeTo (subscribe: Subscribe) : ApiResult<Id> =
        if not (subscriptionsMap.ContainsKey(subscribe.subscriber)) then    
            lock subscriptionsMap <| fun () ->    
                let subscriptions = subscriptionsMap.GetValueOrDefault(subscribe.subscriber, new HashSet<string>())
                subscriptions.Add(subscribe.publisher) |> ignore
                subscriptionsMap.[subscribe.subscriber] <- subscriptions
            lock subscribersMap <| fun () ->
                let subscribers = subscribersMap.GetValueOrDefault(subscribe.publisher, new List<string>())
                subscribers.Add(subscribe.subscriber)
                subscribersMap.[subscribe.publisher] <- subscribers
            Ok {id="200"}
        else userAlreadySubscribed()

    //  For Tweets
    let addTweet (tweet: Tweet) = // add this tweet to the database
        lock tweetsMap <| fun () ->    
            tweetsMap.[tweet.id] <- tweet
        
        lock tweetsByUsername <| fun () ->
            let tweetIds = tweetsByUsername.GetValueOrDefault(tweet.by, new List<string>())
            tweetIds.Add(tweet.id)
            tweetsByUsername.[tweet.by] <- tweetIds

        lock tweetsByHashTag <| fun () -> // add to tweetsByHashTag
            let hashTags = getPatternMatches regexHashTag tweet.text
            for tag in hashTags do
                let tweetIds = tweetsByHashTag.GetValueOrDefault(tag, new List<string>())
                tweetIds.Add(tweet.id)
                tweetsByHashTag.[tag] <- tweetIds

        lock tweetsByMention <| fun () ->
            let mentions = getPatternMatches regexMention tweet.text
            for mention in mentions do
                let tweetIds = tweetsByMention.GetValueOrDefault(mention, new List<string>())
                tweetIds.Add(tweet.id)
                tweetsByMention.[mention] <- tweetIds

    let CreateTweet (data: Tweet) : ApiResult<Id> =
        // incr lastId
        printfn "create tweet called"
        let tweetId = getRandomString maxLenTweetId
        match data.tType with
        | "tweet" -> // if new tweet
            printfn "New Tweet [%s] by [%s]" data.text data.by
            addTweet {data with id=tweetId}
            // pushTweet tweet
        | "retweet" -> // if it is a retweet
            if tweetsMap.ContainsKey(data.reId) then
                let retweet = {data with id=tweetId; text=tweetsMap.Item(data.reId).text}
                printfn "ReTweet [%s] by [%s]" retweet.text retweet.by
                addTweet retweet
        Ok { id =  tweetId}
        // pushTweet retweet # TODO
    
    let SearchTweet query : ApiResult<Tweet[]> =
        printfn "%A %A %A %A" tweetsByUsername tweetsByHashTag tweetsByMention query
        let response = new List<Tweet>()
        match query.qType with
        | "subscription" -> // get tweets from subscription
            for publisher in subscriptionsMap.GetValueOrDefault(query.by, new HashSet<string>()) do
                for tweetId in tweetsByUsername.GetValueOrDefault(publisher, new List<string>()) do
                    response.Add(tweetsMap.[tweetId])
        | "hashtag" -> // get tweets with this hash tag
            let tag = query.qName
            for tweetId in tweetsByHashTag.GetValueOrDefault(tag, new List<string>()) do
                response.Add(tweetsMap.[tweetId])
        | "mention" -> // //  get tweets with this mention
            let mention = query.qName
            for tweetId in tweetsByMention.GetValueOrDefault(mention, new List<string>()) do
                response.Add(tweetsMap.[tweetId])
        response.ToArray () |> Ok
     

module Site =
    open WebSharper.UI
    open WebSharper.UI.Html
    open WebSharper.UI.Server

    // to convert ApiResult type into WebSharper Content type
    let JsonContent (result: ApiResult<'T>) : Async<Content<EndPoint>> =
        match result with
        | Ok value ->
            Content.Json value
        | Error (status, error) ->
            Content.Json error
            |> Content.SetStatus status
        |> Content.WithContentType "application/json"

    // respond to an API endpoint
    let ApiContent (ep: ApiEndPoint) : Async<Content<EndPoint>> =
        match ep with
        | CreateUser user ->
            JsonContent (Backend.CreateUser user)
        | LoginUser user ->
            JsonContent (Backend.LoginUser user)
        | SubscribeTo subscribe ->
            JsonContent (Backend.SubscribeTo subscribe)
        | CreateTweet tweet ->
            JsonContent (Backend.CreateTweet tweet)
        | QueryTweet query ->
            JsonContent (Backend.SearchTweet query)

    // A simple HTML home page.
    let HomePage (ctx: Context<EndPoint>) : Async<Content<EndPoint>> =
        Content.Page(
            Body = [
                p [] [text "API is running."]
            ]
        )

    // to parse requests into EndPoints
    let Main corsAllowedOrigins : Sitelet<EndPoint> =
        Application.MultiPage (fun ctx endpoint ->
            match endpoint with
            | Home -> HomePage ctx
            | Api api ->
                Content.Cors api (fun allows ->
                    { allows with
                        Origins = corsAllowedOrigins
                        Headers = ["Content-Type"]
                    }
                ) ApiContent
        )