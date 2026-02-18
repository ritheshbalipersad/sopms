using SOPMSApp.Controllers;
using System;
using static SOPMSApp.Controllers.ApprovalsController;

namespace SOPMSApp.Controllers
{
    public static class SopApprovalExtensions
    {
        public static void SetManagerApproval(this SopData sop, string reviewer, DateTime time)
        {
            if (sop.StructuredSop != null)
            {
                sop.StructuredSop.Status = "Pending Admin Approval";
                sop.StructuredSop.ApprovalStage = "Manager";
                sop.StructuredSop.ManagerApproved = true;
                sop.StructuredSop.ManagerApprovedDate = time;
                sop.StructuredSop.ReviewedBy = reviewer;
            }

            if (sop.DocRegister != null)
            {
                sop.DocRegister.Status = "Pending Admin Approval";
                sop.DocRegister.ApprovalStage = "Manager";
                sop.DocRegister.ManagerApproved = true;
                sop.DocRegister.ManagerApprovedDate = time;
                sop.DocRegister.ReviewedBy = reviewer;
            }
        }

        public static void SetAdminApproval(this SopData sop, string approver, DateTime time)
        {
            if (sop.StructuredSop != null)
            {
                sop.StructuredSop.Status = "Approved";
                sop.StructuredSop.ReviewStatus = "Approved";
                sop.StructuredSop.ApprovalStage = "Admin";
                sop.StructuredSop.AdminApproved = true;
                sop.StructuredSop.AdminApprovedDate = time;
                sop.StructuredSop.ApprovedBy = approver;
            }

            if (sop.DocRegister != null)
            {
                sop.DocRegister.Status = "Approved";
                sop.DocRegister.ApprovalStage = "Admin";
                sop.DocRegister.AdminApproved = true;
                sop.DocRegister.AdminApprovedDate = time;
                sop.DocRegister.ApprovedBy = approver;

                sop.DocRegister.EffectiveDate =
                    sop.StructuredSop?.EffectiveDate ?? sop.DocRegister.EffectiveDate ?? time;

                sop.DocRegister.Revision =
                    sop.StructuredSop?.Revision ?? sop.DocRegister.Revision ?? "Rev: 0";
            }
        }
    }

}
