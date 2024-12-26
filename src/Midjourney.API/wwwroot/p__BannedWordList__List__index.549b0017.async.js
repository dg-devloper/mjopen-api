"use strict";(self.webpackChunkmidjourney_proxy_admin=self.webpackChunkmidjourney_proxy_admin||[]).push([[636],{47389:function(y,f,e){var M=e(1413),m=e(67294),i=e(27363),u=e(89099),d=function(r,O){return m.createElement(u.Z,(0,M.Z)((0,M.Z)({},r),{},{ref:O,icon:i.Z}))},c=m.forwardRef(d);f.Z=c},51042:function(y,f,e){var M=e(1413),m=e(67294),i=e(42110),u=e(89099),d=function(r,O){return m.createElement(u.Z,(0,M.Z)((0,M.Z)({},r),{},{ref:O,icon:i.Z}))},c=m.forwardRef(d);f.Z=c},88697:function(y,f,e){e.r(f);var M=e(97857),m=e.n(M),i=e(15009),u=e.n(i),d=e(99289),c=e.n(d),g=e(5574),r=e.n(g),O=e(27986),h=e(84436),v=e(66927),n=e(47389),U=e(82061),Z=e(51042),C=e(90930),T=e(51043),W=e(80854),R=e(53025),D=e(16568),j=e(14726),A=e(72269),S=e(42075),x=e(86738),ee=e(4393),I=e(67294),s=e(85893),te=function(){var se=(0,I.useState)(!1),w=r()(se,2),ae=w[0],F=w[1],ne=(0,I.useState)({}),$=r()(ne,2),_e=$[0],V=$[1],re=(0,I.useState)(""),z=r()(re,2),oe=z[0],ie=z[1],de=(0,I.useState)({}),N=r()(de,2),le=N[0],G=N[1],ue=(0,I.useState)(1e3),H=r()(ue,2),me=H[0],Ee=H[1],Me=R.Z.useForm(),ce=r()(Me,1),b=ce[0],_=(0,W.useIntl)(),fe=D.ZP.useNotification(),J=r()(fe,2),L=J[0],ge=J[1],B=(0,I.useRef)(),K=function(){V({}),G({}),F(!1)},Pe=(0,I.useState)(!1),Q=r()(Pe,2),Oe=Q[0],X=Q[1],Y=(0,s.jsxs)(s.Fragment,{children:[(0,s.jsx)(j.ZP,{onClick:K,children:_.formatMessage({id:"pages.cancel"})},"back"),(0,s.jsx)(j.ZP,{type:"primary",loading:Oe,onClick:function(){return b.submit()},children:_.formatMessage({id:"pages.submit"})},"submit")]}),k=function(){var E=c()(u()().mark(function l(t){var o,P;return u()().wrap(function(a){for(;;)switch(a.prev=a.next){case 0:return X(!0),t.keywords||(t.keywords=[]),typeof t.keywords=="string"&&(t.keywords=t.keywords.split(",")),a.next=5,(0,v.XM)(t);case 5:o=a.sent,o.success?(L.success({message:"success",description:o.message}),K(),(P=B.current)===null||P===void 0||P.reload()):L.error({message:"error",description:o.message}),X(!1);case 8:case"end":return a.stop()}},l)}));return function(t){return E.apply(this,arguments)}}(),q=function(l,t,o,P){b.resetFields(),ie(l),V(t),G(o),Ee(P),F(!0)},De=function(){var E=c()(u()().mark(function l(t){var o,P;return u()().wrap(function(a){for(;;)switch(a.prev=a.next){case 0:return a.prev=0,a.next=3,(0,v.Fy)(t);case 3:o=a.sent,o.success?(L.success({message:"success",description:_.formatMessage({id:"pages.task.deleteSuccess"})}),(P=B.current)===null||P===void 0||P.reload()):L.error({message:"error",description:o.message}),a.next=10;break;case 7:a.prev=7,a.t0=a.catch(0),console.error(a.t0);case 10:return a.prev=10,a.finish(10);case 12:case"end":return a.stop()}},l,null,[[0,7,10,12]])}));return function(t){return E.apply(this,arguments)}}(),pe=[{title:_.formatMessage({id:"pages.word.name"}),dataIndex:"name",width:160,align:"left",fixed:"left"},{title:_.formatMessage({id:"pages.word.description"}),dataIndex:"description",width:160,align:"left"},{title:_.formatMessage({id:"pages.word.keywords"}),dataIndex:"keywords",width:120,align:"left",render:function(l,t){return(0,s.jsxs)("span",{children:[(t==null?void 0:t.keywords.length)||"0"," ",_.formatMessage({id:"pages.word.keywords"})]})}},{title:_.formatMessage({id:"pages.word.enable"}),dataIndex:"enable",width:80,showInfo:!1,hideInSearch:!0,align:"left",render:function(l,t){return(0,s.jsx)(A.Z,{disabled:!0,checked:t.enable})}},{title:_.formatMessage({id:"pages.word.weight"}),dataIndex:"weight",width:80,align:"left",hideInSearch:!0},{title:_.formatMessage({id:"pages.word.createTime"}),dataIndex:"createTimeFormat",width:140,ellipsis:!0,hideInSearch:!0},{title:_.formatMessage({id:"pages.word.updateTime"}),dataIndex:"updateTimeFormat",width:140,ellipsis:!0,hideInSearch:!0},{title:_.formatMessage({id:"pages.operation"}),dataIndex:"operation",width:140,key:"operation",fixed:"right",align:"center",hideInSearch:!0,render:function(l,t){return(0,s.jsxs)(S.Z,{children:[(0,s.jsx)(j.ZP,{icon:(0,s.jsx)(n.Z,{}),onClick:function(){return q(_.formatMessage({id:"pages.word.update"}),(0,s.jsx)(h.Z,{form:b,record:t,onSubmit:k}),Y,1e3)}},"Update"),(0,s.jsx)(x.Z,{title:_.formatMessage({id:"pages.word.delete"}),description:_.formatMessage({id:"pages.word.deleteTitle"}),onConfirm:function(){return De(t.id)},children:(0,s.jsx)(j.ZP,{danger:!0,icon:(0,s.jsx)(U.Z,{})})})]})}}];return(0,s.jsxs)(C._z,{children:[ge,(0,s.jsx)(ee.Z,{children:(0,s.jsx)(T.Z,{columns:pe,scroll:{x:1e3},search:{defaultCollapsed:!0},pagination:{pageSize:10,showQuickJumper:!1,showSizeChanger:!1},rowKey:"id",actionRef:B,toolBarRender:function(){return[(0,s.jsx)(j.ZP,{type:"primary",icon:(0,s.jsx)(Z.Z,{}),onClick:function(){return q(_.formatMessage({id:"pages.word.add"}),(0,s.jsx)(h.Z,{form:b,record:{},onSubmit:k}),Y,1e3)},children:_.formatMessage({id:"pages.add"})},"primary")]},request:function(){var E=c()(u()().mark(function l(t){var o;return u()().wrap(function(p){for(;;)switch(p.prev=p.next){case 0:return t.keywords&&typeof t.keywords=="string"&&(t.keywords=t.keywords.split(",")),p.next=3,(0,v.R4)(m()(m()({},t),{},{pageNumber:t.current-1}));case 3:return o=p.sent,p.abrupt("return",{data:o.list,total:o.pagination.total,success:!0});case 5:case"end":return p.stop()}},l)}));return function(l){return E.apply(this,arguments)}}()})}),(0,s.jsx)(O.Z,{title:oe,modalVisible:ae,hideModal:K,modalContent:_e,footer:le,modalWidth:me})]})};f.default=te},84436:function(y,f,e){var M=e(5574),m=e.n(M),i=e(53025),u=e(71230),d=e(15746),c=e(4393),g=e(55102),r=e(72269),O=e(37804),h=e(67294),v=e(80854),n=e(85893),U=function(C){var T=C.form,W=C.onSubmit,R=C.record,D=(0,v.useIntl)(),j=(0,h.useState)(!1),A=m()(j,2),S=A[0],x=A[1];return(0,h.useEffect)(function(){T.setFieldsValue(R),R.id=="admin"?x(!0):x(!1),console.log("record",R)}),(0,n.jsx)(i.Z,{form:T,labelAlign:"left",layout:"horizontal",labelCol:{span:4},wrapperCol:{span:20},onFinish:W,children:(0,n.jsx)(u.Z,{gutter:16,children:(0,n.jsx)(d.Z,{span:24,children:(0,n.jsxs)(c.Z,{type:"inner",children:[(0,n.jsx)(i.Z.Item,{label:"id",name:"id",hidden:!0,children:(0,n.jsx)(g.Z,{})}),(0,n.jsx)(i.Z.Item,{required:!0,label:D.formatMessage({id:"pages.domain.name"}),name:"name",children:(0,n.jsx)(g.Z,{})}),(0,n.jsx)(i.Z.Item,{label:D.formatMessage({id:"pages.domain.description"}),name:"description",children:(0,n.jsx)(g.Z,{})}),(0,n.jsx)(i.Z.Item,{label:D.formatMessage({id:"pages.domain.enable"}),name:"enable",children:(0,n.jsx)(r.Z,{})}),(0,n.jsx)(i.Z.Item,{label:D.formatMessage({id:"pages.domain.sort"}),name:"sort",children:(0,n.jsx)(O.Z,{})}),(0,n.jsx)(i.Z.Item,{label:D.formatMessage({id:"pages.domain.keywords"}),name:"keywords",tooltip:D.formatMessage({id:"pages.domain.keywords.tooltip"}),children:(0,n.jsx)(g.Z.TextArea,{placeholder:"cat, dog, fish",autoSize:{minRows:1,maxRows:20},allowClear:!0})})]})})})})};f.Z=U},27986:function(y,f,e){var M=e(17910),m=e(85893),i=function(d){var c=d.title,g=d.modalVisible,r=d.hideModal,O=d.modalContent,h=d.footer,v=d.modalWidth;return(0,m.jsx)(M.Z,{title:c,open:g,onCancel:r,footer:h,width:v,children:O})};f.Z=i}}]);
