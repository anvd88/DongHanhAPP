package com.ketoanapk.hr.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Campaign
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject

@Composable fun SurveyFeedbackScreen(vm:HrViewModel){var open by remember{mutableStateOf<com.ketoanapk.hr.data.SurveyItem?>(null)};var message by remember{mutableStateOf("")};var anonymous by remember{mutableStateOf(false)}
    LazyColumn(Modifier.fillMaxSize(),contentPadding=PaddingValues(14.dp),verticalArrangement=Arrangement.spacedBy(10.dp)){
        item{HrCard{Text("Gửi góp ý",fontWeight=FontWeight.Bold);OutlinedTextField(message,{message=it},minLines=3,modifier=Modifier.fillMaxWidth());Row{Checkbox(anonymous,{anonymous=it});Text("Gửi ẩn danh",modifier=Modifier.padding(top=12.dp))};Button(enabled=message.isNotBlank(),onClick={vm.sendGeneralFeedback(message,anonymous);message=""},modifier=Modifier.fillMaxWidth()){Text("Gửi góp ý")}}}
        item{SectionTitle("Khảo sát đang mở")};if(vm.surveys.isEmpty())item{EmptyState("Không có khảo sát","Hiện chưa có khảo sát đang mở.")}else items(vm.surveys,key={it.id}){s->HrCard{Text(s.title,fontWeight=FontWeight.Bold);Text(s.description,style=MaterialTheme.typography.bodySmall);s.closesAt?.let{Text("Đóng ${formatIsoDateTime(it)}",style=MaterialTheme.typography.bodySmall)};Button({open=s},enabled=!s.answered,modifier=Modifier.fillMaxWidth()){Text(if(s.answered)"Đã trả lời" else "Trả lời / bình chọn")}}}
        item{SectionTitle("Phản hồi của tôi")};items(vm.myFeedback,key={it.id}){f->HrCard{Text(f.message,fontWeight=FontWeight.Bold);StatusChip(if(f.status=="resolved")"Đã xử lý" else "Đang xử lý",if(f.status=="resolved")Tone.Success else Tone.Warning);if(f.response.isNotBlank())Text(f.response)}}
    }
    open?.let{s->SurveyDialog(s,{open=null}){vm.answerSurvey(s.id,it);open=null}}
}
@Composable private fun SurveyDialog(s:com.ketoanapk.hr.data.SurveyItem,close:()->Unit,submit:(kotlinx.serialization.json.JsonObject)->Unit){val answers=remember{mutableStateMapOf<String,String>()};AlertDialog(onDismissRequest=close,title={Text(s.title)},text={Column(Modifier.heightIn(max=500.dp)){s.questions.forEach{q->Text(q.text,fontWeight=FontWeight.Bold);if(q.options.isEmpty())OutlinedTextField(answers[q.key].orEmpty(),{answers[q.key]=it},modifier=Modifier.fillMaxWidth())else q.options.forEach{o->FilterChip(answers[q.key]==o,{answers[q.key]=o},{Text(o)})}}}},confirmButton={Button(enabled=s.questions.all{!answers[it.key].isNullOrBlank()},onClick={submit(buildJsonObject{answers.forEach{(k,v)->put(k,JsonPrimitive(v))}})}){Text("Gửi")}},dismissButton={TextButton(close){Text("Hủy")}})}
