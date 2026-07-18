package com.ketoanapk.hr.ui

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Campaign
import androidx.compose.material.icons.filled.Checklist
import androidx.compose.material.icons.filled.School
import androidx.compose.material.icons.filled.CardGiftcard
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.ketoanapk.hr.data.*
import com.ketoanapk.hr.ui.theme.Warning
import java.time.Instant

@Composable fun OnboardingScreen(vm:HrViewModel){ val s=vm.talentState; val data=s.onboarding
    LazyColumn(Modifier.fillMaxSize(),contentPadding=PaddingValues(16.dp),verticalArrangement=Arrangement.spacedBy(10.dp)){
        if(s.loading&&data==null)item{LoadingBlock()}; if(s.error!=null&&data==null)item{EmptyState("Không tải được",s.error)}
        data?.let{d-> val done=d.items.count{it.completed};item{HrCard{Text("Hoàn thành $done/${d.items.size}",fontWeight=FontWeight.Bold);LinearProgressIndicator(progress={if(d.items.isEmpty())0f else done.toFloat()/d.items.size},modifier=Modifier.fillMaxWidth())}}
            if(d.items.isEmpty())item{EmptyState("Chưa có checklist","HR chưa giao nhiệm vụ onboarding.")}
            items(d.items,key={it.id}){t->HrCard{Text(t.title,fontWeight=FontWeight.Bold);t.dueAt?.let{Text("Hạn: ${formatIsoDateTime(it)}",color=if(runCatching{Instant.parse(it).isBefore(Instant.now())}.getOrDefault(false)&&!t.completed)Warning else MaterialTheme.colorScheme.onSurfaceVariant)};if(t.policyText.isNotBlank())Text(t.policyText,style=MaterialTheme.typography.bodySmall);Button({vm.completeOnboarding(t.id)},enabled=!t.completed,modifier=Modifier.fillMaxWidth()){Text(if(t.completed)"Đã hoàn thành" else if(t.policyText.isNotBlank())"Đã đọc và xác nhận" else "Đánh dấu hoàn thành")}}}
        }
    }}

@Composable fun PerformanceScreen(vm:HrViewModel){val s=vm.talentState;val d=s.performance;var review by remember{mutableStateOf<PerformanceReview?>(null)};var text by remember{mutableStateOf("")}
    LazyColumn(Modifier.fillMaxSize(),contentPadding=PaddingValues(16.dp),verticalArrangement=Arrangement.spacedBy(10.dp)){
        if(s.loading&&d==null)item{LoadingBlock()};if(d==null&&!s.loading)item{EmptyState("Chưa có dữ liệu",s.error?:"HR chưa thiết lập mục tiêu.")}
        d?.let{data->item{SectionTitle("Mục tiêu hiện tại")};items(data.goals,key={it.id}){g->GoalCard(g){vm.updateGoal(g.id,it)}};item{SectionTitle("Các kỳ đánh giá")};items(data.reviews,key={it.id}){r->HrCard{Text(r.period,fontWeight=FontWeight.Bold);r.score?.let{LabelValue("Điểm",it.toString())};if(r.managerComment.isNotBlank())LabelValue("Nhận xét quản lý",r.managerComment);r.closesAt?.let{Text("Đóng lúc ${formatIsoDateTime(it)}",style=MaterialTheme.typography.bodySmall)};if(r.status=="open")Button({review=r;text=r.selfAssessment},Modifier.fillMaxWidth()){Text("Tự đánh giá")}}}}
    }
    review?.let{r->AlertDialog(onDismissRequest={review=null},title={Text("Tự đánh giá ${r.period}")},text={OutlinedTextField(text,{text=it},minLines=5,modifier=Modifier.fillMaxWidth())},confirmButton={Button({vm.submitSelfReview(r.id,text);review=null}){Text("Lưu")}},dismissButton={TextButton({review=null}){Text("Hủy")}})}
}

@Composable private fun GoalCard(g:PerformanceGoal,onUpdate:(Double)->Unit){var value by remember(g.id,g.progress){mutableStateOf(g.progress.toInt().toString())};HrCard{Text(g.title,fontWeight=FontWeight.Bold);if(g.description.isNotBlank())Text(g.description,style=MaterialTheme.typography.bodySmall);LinearProgressIndicator(progress={if(g.target<=0)0f else (g.progress/g.target).toFloat().coerceIn(0f,1f)},modifier=Modifier.fillMaxWidth());Text("${g.progress}/${g.target} ${g.unit}");Row{OutlinedTextField(value,{value=it.filter(Char::isDigit)},label={Text("Tiến độ")},modifier=Modifier.weight(1f));Button({onUpdate(value.toDoubleOrNull()?:g.progress)},Modifier.padding(start=8.dp,top=8.dp)){Text("Cập nhật")}}}}

@Composable fun TrainingScreen(vm:HrViewModel){val s=vm.talentState;var quiz by remember{mutableStateOf<TrainingCourse?>(null)};val context=LocalContext.current
    LazyColumn(Modifier.fillMaxSize(),contentPadding=PaddingValues(16.dp),verticalArrangement=Arrangement.spacedBy(10.dp)){
        if(s.loading&&s.training.isEmpty())item{LoadingBlock()};if(!s.loading&&s.training.isEmpty())item{EmptyState("Chưa có khóa học",s.error?:"HR chưa phát hành khóa học.")}
        items(s.training,key={it.id}){c->HrCard{Text(c.title,fontWeight=FontWeight.Bold);Text(c.description,style=MaterialTheme.typography.bodySmall);LinearProgressIndicator(progress={c.progress/100f},modifier=Modifier.fillMaxWidth());Text("Tiến độ ${c.progress}% · tiếp tục từ ${c.resumeSeconds/60} phút",style=MaterialTheme.typography.bodySmall)
            Row(horizontalArrangement=Arrangement.spacedBy(6.dp)){if(c.materialUrl.isNotBlank())TextButton({context.startActivity(Intent(Intent.ACTION_VIEW,Uri.parse(c.materialUrl)))}){Text("Tài liệu")};if(c.videoUrl.isNotBlank())TextButton({context.startActivity(Intent(Intent.ACTION_VIEW,Uri.parse(c.videoUrl)))}){Text("Video")};if(c.quiz.isNotEmpty())TextButton({quiz=c}){Text("Kiểm tra")}}
            Button({vm.updateTraining(c.id,(c.progress+10).coerceAtMost(100),c.resumeSeconds+300)},Modifier.fillMaxWidth()){Text("Tiếp tục học")};c.score?.let{LabelValue("Điểm", "$it%")} ;c.certificateExpiresAt?.let{Text("Chứng nhận hết hạn ${formatIsoDate(it)}",color=Warning)}}}
    }
    quiz?.let{course->QuizDialog(course,{quiz=null}){vm.submitTrainingQuiz(course.id,it);quiz=null}}
}

@Composable private fun QuizDialog(course:TrainingCourse,close:()->Unit,submit:(List<String>)->Unit){val answers=remember{mutableStateMapOf<Int,String>()};AlertDialog(onDismissRequest=close,title={Text("Kiểm tra: ${course.title}")},text={Column(Modifier.heightIn(max=500.dp)){course.quiz.forEachIndexed{i,q->Text(q.text,fontWeight=FontWeight.Bold);q.options.forEach{o->FilterChip(answers[i]==o,{answers[i]=o},{Text(o)})}}}},confirmButton={Button(enabled=answers.size==course.quiz.size,onClick={submit(course.quiz.indices.map{answers[it].orEmpty()})}){Text("Nộp bài")}},dismissButton={TextButton(close){Text("Hủy")}})}

@Composable
fun BenefitsScreen(vm: HrViewModel) {
    val state = vm.talentState
    val data = state.benefits
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        if (state.loading && data == null) item { LoadingBlock() }
        if (!state.loading && data == null) item { EmptyState("Không tải được phúc lợi", state.error ?: "Chưa có dữ liệu.") }
        if (data != null) {
            item {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                    StatTile(Icons.Filled.CardGiftcard, "Phép còn", data.leaveRemaining.toString(), com.ketoanapk.hr.ui.theme.Success, Modifier.weight(1f))
                    StatTile(Icons.Filled.CardGiftcard, "Đã dùng", data.leaveUsed.toString(), Warning, Modifier.weight(1f))
                }
            }
            if (data.birthday != null) item {
                HrCard {
                    LabelValue("Sinh nhật", formatIsoDate(data.birthday))
                    data.hireDate?.let {
                        LabelValue("Ngày vào làm", formatIsoDate(it))
                        val years = runCatching { java.time.Period.between(java.time.LocalDate.parse(it.take(10)), java.time.LocalDate.now()).years }.getOrDefault(0)
                        Text("Thâm niên $years năm", fontWeight = FontWeight.Bold)
                    }
                }
            }
            item { SectionTitle("Bảo hiểm, khám sức khỏe & phụ cấp") }
            if (data.benefits.isEmpty()) item { EmptyState("Chưa có quyền lợi bổ sung", "HR chưa cập nhật bảo hiểm, khám sức khỏe hoặc phụ cấp.") }
            else items(data.benefits, key = { it.id }) { x -> HrCard { Text(x.title, fontWeight = FontWeight.Bold); Text(x.value); x.validTo?.let { Text("Hiệu lực đến ${formatIsoDate(it)}", style = MaterialTheme.typography.bodySmall) } } }
            item { SectionTitle("Khen thưởng & điểm") }
            if (data.rewards.isEmpty()) item { EmptyState("Chưa có khen thưởng", "Các ghi nhận và điểm thưởng sẽ hiển thị tại đây.") }
            else items(data.rewards, key = { it.id }) { x -> HrCard { Text(x.title, fontWeight = FontWeight.Bold); LabelValue("Điểm", "${x.points}"); Text(formatIsoDate(x.awardedAt), style = MaterialTheme.typography.bodySmall) } }
            item { SectionTitle("Lịch sử nghỉ phép") }
            items(data.leaveHistory, key = { it.requestNo }) { x -> HrCard { Text(x.requestNo, fontWeight = FontWeight.Bold); Text(requestStatusLabel(x.status)); Text(formatIsoDateTime(x.createdAt), style = MaterialTheme.typography.bodySmall) } }
        }
    }
}
