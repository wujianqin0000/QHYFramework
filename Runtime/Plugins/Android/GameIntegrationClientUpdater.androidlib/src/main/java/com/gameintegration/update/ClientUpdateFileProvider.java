package com.gameintegration.update;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;
import java.io.File;
import java.io.FileNotFoundException;
import java.io.IOException;

public final class ClientUpdateFileProvider extends ContentProvider {
    private File resolve(Uri uri) throws FileNotFoundException {
        String name = uri.getLastPathSegment();
        if (name == null || name.length() == 0 || !name.equals(new File(name).getName()))
            throw new FileNotFoundException("Invalid client update file name");
        File root = new File(getContext().getExternalFilesDir(null), "ClientUpdates");
        File file = new File(root, name);
        try {
            String rootPath = root.getCanonicalPath() + File.separator;
            if (!file.getCanonicalPath().startsWith(rootPath))
                throw new FileNotFoundException("Path escapes client update directory");
        } catch (IOException exception) {
            throw new FileNotFoundException(exception.getMessage());
        }
        if (!file.isFile()) throw new FileNotFoundException(file.getAbsolutePath());
        return file;
    }

    @Override public boolean onCreate() { return true; }
    @Override public String getType(Uri uri) { return "application/vnd.android.package-archive"; }
    @Override public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        if (!"r".equals(mode)) throw new FileNotFoundException("Read-only provider");
        return ParcelFileDescriptor.open(resolve(uri), ParcelFileDescriptor.MODE_READ_ONLY);
    }
    @Override public Cursor query(Uri uri, String[] projection, String selection,
                                  String[] selectionArgs, String sortOrder) {
        try {
            File file = resolve(uri);
            MatrixCursor cursor = new MatrixCursor(new String[] { OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE });
            cursor.addRow(new Object[] { file.getName(), file.length() });
            return cursor;
        } catch (FileNotFoundException exception) {
            return new MatrixCursor(new String[] { OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE });
        }
    }
    @Override public Uri insert(Uri uri, ContentValues values) { throw new UnsupportedOperationException(); }
    @Override public int delete(Uri uri, String selection, String[] selectionArgs) { return 0; }
    @Override public int update(Uri uri, ContentValues values, String selection, String[] selectionArgs) { return 0; }
}
