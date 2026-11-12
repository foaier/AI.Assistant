import { ApiClient } from '@/lib/ApiClient';

/** Optional image rendition metadata retained for Assistant presentation assets. */
export type AssetDerivativeDto = {
  url: string;
  width: number;
  height: number;
  sizeBytes: number;
};

export type AssetDto = {
  assetId: string;
  url: string;
  publicUrl: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  category: string;
  eventId: string | null;
  /** Use the original image when no rendition is available. */
  derivatives?: Record<string, AssetDerivativeDto> | null;
};

export type UploadAssetOptions = {
  category: string;
  eventId?: string;
};

export async function uploadAsset(
  file: File,
  options: UploadAssetOptions,
): Promise<AssetDto> {
  const formData = new FormData();
  formData.append('file', file);
  formData.append('category', options.category);
  if (options.eventId) {
    formData.append('eventId', options.eventId);
  }

  return new ApiClient('/api').postFormData<AssetDto>('/assets/upload', formData);
}

// Assistant image controls accept JPEG, PNG or WebP images up to 10 MB.
export const ASSET_IMAGE_MAX_BYTES = 10 * 1024 * 1024;
export const ASSET_IMAGE_ALLOWED_TYPES: readonly string[] = ['image/jpeg', 'image/png', 'image/webp'];
