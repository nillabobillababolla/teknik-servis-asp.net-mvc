﻿using Microsoft.AspNet.Identity;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Helpers;
using System.Web.Mvc;
using TeknikServis.BLL.Helpers;
using TeknikServis.BLL.Repository;
using TeknikServis.BLL.Services.Senders;
using TeknikServis.Models.Entities;
using TeknikServis.Models.Enums;
using TeknikServis.Models.Models;
using TeknikServis.Models.ViewModels;
using static TeknikServis.BLL.Identity.MembershipTools;

namespace TeknikServis.Web.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : BaseController
    {
        private readonly IssueRepo issueRepo;
        public AdminController()
        {
            issueRepo = new IssueRepo();
        }

        [HttpGet]
        public ActionResult Index()
        {
            return View(NewUserStore().Users.ToList());
        }

        [HttpPost]
        public async Task<JsonResult> SendCode(string id)
        {
            try
            {
                var userStore = NewUserStore();
                var user = await userStore.FindByIdAsync(id);
                if (user == null)
                {
                    return Json(new ResponseData()
                    {
                        message = "Kullanıcı bulunamadı.",
                        success = false
                    });
                }

                if (user.EmailConfirmed)
                {
                    return Json(new ResponseData()
                    {
                        message = "Kullanıcı zaten e-postasını onaylamış.",
                        success = false
                    });
                }

                user.ActivationCode = StringHelpers.GetCode();
                await userStore.UpdateAsync(user);
                userStore.Context.SaveChanges();
                string SiteUrl = Request.Url.Scheme + System.Uri.SchemeDelimiter + Request.Url.Host +
                                 (Request.Url.IsDefaultPort ? "" : ":" + Request.Url.Port);

                var emailService = new EmailService();
                var body =
                    $"Merhaba <b>{user.Name} {user.Surname}</b><br>Hesabınızı aktif etmek için aşağıdaki linke tıklayınız.<br> <a href='{SiteUrl}/account/activation?code={user.ActivationCode}' >Aktivasyon Linki </a> ";
                await emailService.SendAsync(new IdentityMessage()
                {
                    Body = body,
                    Subject = "Sitemize Hoşgeldiniz"
                }, user.Email);
                return Json(new ResponseData()
                {
                    message = "Kullanıcıya yeni aktivasyon maili gönderildi.",
                    success = true
                });
            }
            catch (Exception ex)
            {
                return Json(new ResponseData()
                {
                    message = $"Bir hata oluştu: {ex.Message}",
                    success = false
                });
            }
        }

        [HttpPost]
        public async Task<JsonResult> SendPassword(string id)
        {
            try
            {
                var userStore = NewUserStore();
                var user = await userStore.FindByIdAsync(id);
                if (user == null)
                {
                    return Json(new ResponseData()
                    {
                        message = "Kullanıcı bulunamadı",
                        success = false
                    });
                }

                var newPassword = StringHelpers.GetCode().Substring(0, 6);
                await userStore.SetPasswordHashAsync(user, NewUserManager().PasswordHasher.HashPassword(newPassword));
                await userStore.UpdateAsync(user);
                userStore.Context.SaveChanges();

                string SiteUrl = Request.Url.Scheme + System.Uri.SchemeDelimiter + Request.Url.Host +
                                 (Request.Url.IsDefaultPort ? "" : ":" + Request.Url.Port);
                var emailService = new EmailService();
                var body =
                    $"Merhaba <b>{user.Name} {user.Surname}</b><br>Hesabınızın parolası sıfırlanmıştır<br> Yeni parolanız: <b>{newPassword}</b> <p>Yukarıdaki parolayı kullanarak sistemize giriş yapabilirsiniz.</p>";
                emailService.Send(new IdentityMessage() { Body = body, Subject = $"{user.UserName} Şifre Kurtarma" },
                    user.Email);

                return Json(new ResponseData()
                {
                    message = "Şifre sıfırlama maili gönderilmiştir.",
                    success = true
                });
            }
            catch (Exception ex)
            {
                return Json(new ResponseData()
                {
                    message = $"Bir hata oluştu: {ex.Message}",
                    success = false
                });
            }
        }

        [HttpGet]
        public ActionResult EditUser(string id)
        {
            try
            {
                var user = NewUserManager().FindById(id);
                if (user == null)
                {
                    return RedirectToAction("Index");
                }

                var roller = GetRoleList();
                foreach (var role in user.Roles)
                {
                    foreach (var selectListItem in roller)
                    {
                        if (selectListItem.Value == role.RoleId)
                        {
                            selectListItem.Selected = true;
                        }
                    }
                }

                ViewBag.RoleList = roller;


                var model = new UserProfileVM()
                {
                    AvatarPath = user.AvatarPath,
                    Name = user.Name,
                    Email = user.Email,
                    Surname = user.Surname,
                    Id = user.Id,
                    PhoneNumber = user.PhoneNumber,
                    UserName = user.UserName
                };
                return View(model);
            }
            catch (Exception ex)
            {
                TempData["Message"] = new ErrorVM()
                {
                    Text = $"Bir hata oluştu {ex.Message}",
                    ActionName = "EditUser",
                    ControllerName = "Admin",
                    ErrorCode = 500
                };
                return RedirectToAction("Error500", "Home");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> EditUser(UserProfileVM model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var userManager = NewUserManager();
                var user = await userManager.FindByIdAsync(model.Id);

                user.Name = model.Name;
                user.Surname = model.Surname;
                user.PhoneNumber = model.PhoneNumber;
                user.Email = model.Email;

                if (model.PostedFile != null &&
                    model.PostedFile.ContentLength > 0)
                {
                    var file = model.PostedFile;
                    string fileName = Path.GetFileNameWithoutExtension(file.FileName);
                    string extName = Path.GetExtension(file.FileName);
                    fileName = StringHelpers.UrlFormatConverter(fileName);
                    fileName += StringHelpers.GetCode();
                    var klasoryolu = Server.MapPath("~/Upload/");
                    var dosyayolu = Server.MapPath("~/Upload/") + fileName + extName;

                    if (!Directory.Exists(klasoryolu))
                    {
                        Directory.CreateDirectory(klasoryolu);
                    }

                    file.SaveAs(dosyayolu);

                    WebImage img = new WebImage(dosyayolu);
                    img.Resize(250, 250, false);
                    img.AddTextWatermark("TeknikServis");
                    img.Save(dosyayolu);
                    var oldPath = user.AvatarPath;
                    user.AvatarPath = "/Upload/" + fileName + extName;

                    System.IO.File.Delete(Server.MapPath(oldPath));
                }

                await userManager.UpdateAsync(user);
                TempData["Message"] = "Güncelleme işlemi başarılı";
                return RedirectToAction("EditUser", new { id = user.Id });
            }
            catch (Exception ex)
            {
                TempData["Message"] = new ErrorVM()
                {
                    Text = $"Bir hata oluştu {ex.Message}",
                    ActionName = "Index",
                    ControllerName = "Admin",
                    ErrorCode = 500
                };
                return RedirectToAction("Error500", "Home");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditUserRoles(UpdateUserRoleVM model)
        {
            //var userId = Request.Form[1].ToString();
            //var rolIdler = Request.Form[2].ToString().Split(',');
            var userId = model.Id;
            var rolIdler = model.Roles;
            var roleManager = NewRoleManager();
            var seciliRoller = new string[rolIdler.Count];
            for (var i = 0; i < rolIdler.Count; i++)
            {
                var rid = rolIdler[i];
                seciliRoller[i] = roleManager.FindById(rid).Name;
            }

            var userManager = NewUserManager();
            var user = userManager.FindById(userId);

            foreach (var identityUserRole in user.Roles.ToList())
            {
                userManager.RemoveFromRole(userId, roleManager.FindById(identityUserRole.RoleId).Name);
            }

            for (int i = 0; i < seciliRoller.Length; i++)
            {
                userManager.AddToRole(userId, seciliRoller[i]);
            }

            return RedirectToAction("EditUser", new { id = userId });
        }

        [HttpGet]
        public ActionResult Reports()
        {
            try
            {
                var issueRepo = new IssueRepo();
                var surveyRepo = new SurveyRepo();
                var issueList = issueRepo.GetAll(x => x.SurveyId != null).ToList();

                var surveyList = surveyRepo.GetAll().Where(x => x.IsDone).ToList();
                var totalSpeed = 0.0;
                var totalTech = 0.0;
                var totalPrice = 0.0;
                var totalSatisfaction = 0.0;
                var totalSolving = 0.0;
                var count = issueList.Count;

                if (count == 0)
                {
                    TempData["Message2"] = "Rapor oluşturmak için yeterli kayıt bulunamadı.";
                    return RedirectToAction("Index", "Home");
                }

                foreach (var survey in surveyList)
                {
                    totalSpeed += survey.Speed;
                    totalTech += survey.TechPoint;
                    totalPrice += survey.Pricing;
                    totalSatisfaction += survey.Satisfaction;
                    totalSolving += survey.Solving;
                }

                var totalDays = 0;
                foreach (var issue in issueList)
                {
                    totalDays += issue.ClosedDate.Value.DayOfYear - issue.CreatedDate.DayOfYear;
                }

                ViewBag.AvgSpeed = totalSpeed / count;
                ViewBag.AvgTech = totalTech / count;
                ViewBag.AvgPrice = totalPrice / count;
                ViewBag.AvgSatisfaction = totalSatisfaction / count;
                ViewBag.AvgSolving = totalSolving / count;
                ViewBag.AvgTime = totalDays / issueList.Count;

                return View(surveyList);
            }
            catch (Exception ex)
            {
                TempData["Message"] = new ErrorVM()
                {
                    Text = $"Bir hata oluştu {ex.Message}",
                    ActionName = "Reports",
                    ControllerName = "Admin",
                    ErrorCode = 500
                };
                return RedirectToAction("Error500", "Home");
            }
        }

        [HttpGet]
        public JsonResult GetDailyReport()
        {
            try
            {
                var dailyIssues = issueRepo.GetAll(x => x.CreatedDate.DayOfYear == DateTime.Now.DayOfYear).Count;

                return Json(new ResponseData()
                {
                    data = dailyIssues,
                    success = true
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new ResponseData()
                {
                    data = 0,
                    message = ex.Message,
                    success = false
                }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public JsonResult GetWeeklyReport()
        {
            try
            {
                List<WeeklyReport> weeklies = new List<WeeklyReport>();

                for (int i = 6; i >= 0; i--)
                {
                    var data = issueRepo.GetAll(x => x.CreatedDate.DayOfYear == DateTime.Now.AddDays(-i).DayOfYear).Count();
                    weeklies.Add(new WeeklyReport()
                    {
                        date = DateTime.Now.AddDays(-i).ToShortDateString(),
                        count = data
                    });
                }

                return Json(new ResponseData()
                {
                    data = weeklies,
                    success = true
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new ResponseData()
                {
                    message = ex.Message,
                    success = false
                }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public JsonResult GetDailyProfit()
        {
            try
            {
                var dailyIssues = issueRepo.GetAll(x => x.CreatedDate.DayOfYear == DateTime.Now.DayOfYear && x.ClosedDate != null);

                decimal data = 0;
                foreach (var item in dailyIssues)
                {
                    data += item.ServiceCharge;
                }
                return Json(new ResponseData()
                {
                    data = data,
                    success = true
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new ResponseData()
                {
                    message = ex.Message,
                    success = false
                }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public JsonResult GetSurveyReport()
        {
            try
            {
                var surveys = new SurveyRepo();
                var count = surveys.GetAll().Count;
                var quest1 = surveys.GetAll().Select(x => x.Satisfaction).Sum() / count;
                var quest2 = surveys.GetAll().Select(x => x.TechPoint).Sum() / count;
                var quest3 = surveys.GetAll().Select(x => x.Speed).Sum() / count;
                var quest4 = surveys.GetAll().Select(x => x.Pricing).Sum() / count;
                var quest5 = surveys.GetAll().Select(x => x.Solving).Sum() / count;

                var data = new List<SurveyReport>();
                data.Add(new SurveyReport()
                {
                    question = "Genel Memnuniyet",
                    point = quest1
                });
                data.Add(new SurveyReport()
                {
                    question = "Teknisyen",
                    point = quest2
                });
                data.Add(new SurveyReport()
                {
                    question = "Hız",
                    point = quest3
                });
                data.Add(new SurveyReport()
                {
                    question = "Fiyat",
                    point = quest4
                });
                data.Add(new SurveyReport()
                {
                    question = "Çözüm Odaklılık",
                    point = quest5
                });

                return Json(new ResponseData()
                {
                    message = $"{data.Count} adet kayıt bulundu",
                    success = true,
                    data = data
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new ResponseData()
                {
                    message = "Kayıt bulunamadı " + ex.Message,
                    success = false
                }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public JsonResult GetTechReport()
        {
            try
            {
                var userManager = NewUserManager();
                var users = userManager.Users.ToList();
                var data = new List<TechReport>();
                foreach (var user in users)
                {
                    if (userManager.IsInRole(user.Id, IdentityRoles.Technician.ToString()))
                    {
                        var techIssues = new IssueRepo().GetAll(x => x.TechnicianId == user.Id);
                        foreach (var issue in techIssues)
                        {
                            if (issue.ClosedDate != null)
                            {
                                data.Add(new TechReport()
                                {
                                    nameSurname = GetNameSurname(user.Id),
                                    point = double.Parse(GetTechPoint(user.Id))
                                });
                            }
                        }
                    }
                }

                return Json(new ResponseData()
                {
                    success = true,
                    data = data
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new ResponseData()
                {
                    message = $"{ex.Message}",
                    success = false
                }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public JsonResult GetBestTech()
        {
            try
            {
                var userManager = NewUserManager();
                var users = userManager.Users.ToList();
                var unclosed = issueRepo.GetAll(x => x.ClosedDate != null);
                var minutes = unclosed.Min(x => x.ClosedDate?.Minute - x.CreatedDate.Minute);
                var data = new Issue();
                foreach (var user in users)
                {
                    if (userManager.IsInRole(user.Id, IdentityRoles.Technician.ToString()))
                    {
                        data = issueRepo.GetAll(x => x.TechnicianId == user.Id && x.ClosedDate?.Minute - x.CreatedDate.Minute == minutes).FirstOrDefault();
                        if (data != null)
                            break;
                    }
                }

                return Json(new ResponseData()
                {
                    data = $"{GetNameSurname(data.TechnicianId)} ({minutes} dk)",
                    success = true,
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new ResponseData()
                {
                    message = $"{ex.Message}",
                    success = false
                }, JsonRequestBehavior.AllowGet);
            }
        }
    }
}
