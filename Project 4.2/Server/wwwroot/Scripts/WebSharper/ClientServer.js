(function()
{
 "use strict";
 var Global,ClientServer,Client,Tweet,SC$1,WebSharper,UI,View,Var$1,Doc,AttrProxy,Client$1,Templates;
 Global=self;
 ClientServer=Global.ClientServer=Global.ClientServer||{};
 Client=ClientServer.Client=ClientServer.Client||{};
 Tweet=Client.Tweet=Client.Tweet||{};
 SC$1=Global.StartupCode$ClientServer$Client=Global.StartupCode$ClientServer$Client||{};
 WebSharper=Global.WebSharper;
 UI=WebSharper&&WebSharper.UI;
 View=UI&&UI.View;
 Var$1=UI&&UI.Var$1;
 Doc=UI&&UI.Doc;
 AttrProxy=UI&&UI.AttrProxy;
 Client$1=UI&&UI.Client;
 Templates=Client$1&&Client$1.Templates;
 Tweet.New=function(id,reId,text,tType,by)
 {
  return{
   id:id,
   reId:reId,
   text:text,
   tType:tType,
   by:by
  };
 };
 Client.Main=function()
 {
  SC$1.$cctor();
  return SC$1.Main;
 };
 Client.op_LessMultiplyGreater=function(f,x)
 {
  return View.Apply(f,x);
 };
 SC$1.$cctor=function()
 {
  var varUsername,inputUsername;
  SC$1.$cctor=Global.ignore;
  SC$1.Main=(varUsername=Var$1.Create$1("username"),(inputUsername=Doc.Input([AttrProxy.Create("name","username")],varUsername),(Templates.LoadLocalTemplates(""),Doc.RunById("main",inputUsername))));
 };
}());
